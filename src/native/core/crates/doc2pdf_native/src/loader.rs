//! Minimal, high-performance document loader for doc2pdf_native.
//! Replaces efizzy-engine's interactive desktop dependency with direct format decoders.

use efizzy_doc::Document;
use efizzy_docbin::DocbinError;

const ZIP_SIG: &[u8] = b"PK\x03\x04";
const OLE_SIG: &[u8] = &[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

/// Open a document buffer by extension with optional password.
pub fn open_bytes_with(
    ext: &str,
    bytes: &[u8],
    password: Option<&str>,
) -> Result<(Document, bool), String> {
    let region = efizzy_geom::Region::default();
    let (_, w, h) = region.paper();
    let page = efizzy_doc::SectionProps {
        page_w: w,
        page_h: h,
        ..Default::default()
    };

    let (mut doc, encrypted) = read_bytes(ext, bytes, password, &page)?;
    load_embedded_fonts(&doc);
    doc.ensure_nonempty();
    Ok((doc, encrypted))
}

fn open_binary_doc(bytes: &[u8], password: Option<&str>) -> Result<(Document, bool), String> {
    let encrypted = efizzy_docbin::is_encrypted(bytes);
    match efizzy_docbin::read_with_password(bytes, password) {
        Ok(doc) => Ok((doc, encrypted)),
        Err(DocbinError::PasswordRequired) => Err(efizzy_docx::PASSWORD_REQUIRED.to_string()),
        Err(DocbinError::WrongPassword) => Err(efizzy_docx::WRONG_PASSWORD.to_string()),
        Err(e) => Err(e.to_string()),
    }
}

fn read_bytes(
    ext: &str,
    bytes: &[u8],
    password: Option<&str>,
    page: &efizzy_doc::SectionProps,
) -> Result<(Document, bool), String> {
    let clean_ext = ext.trim_start_matches('.').to_ascii_lowercase();

    // Check encrypted OOXML packages
    if matches!(clean_ext.as_str(), "docx" | "docm" | "dotx" | "dotm" | "doc" | "dot")
        && efizzy_docx::is_encrypted(bytes)
    {
        let doc = efizzy_docx::read_with_password(bytes, password).map_err(|e| e.to_string())?;
        return Ok((doc, true));
    }

    // Check signature contradictions (e.g. RTF or OLE disguised as DOCX)
    if clean_ext.is_empty() || signature_contradicts(&clean_ext, bytes) {
        return open_by_content(&clean_ext, bytes, password, page);
    }

    let doc = match clean_ext.as_str() {
        "doc" | "dot" => {
            return open_binary_doc(bytes, password);
        }
        "docx" | "docm" | "dotx" | "dotm" => {
            efizzy_docx::read(bytes).map_err(|e| e.to_string())?
        }
        "xml" => efizzy_docx::read_xml(bytes).map_err(|e| e.to_string())?,
        "txt" | "text" => efizzy_formats::txt::import_on(bytes, page),
        "json" => {
            let mut d = serde_json::from_slice::<Document>(bytes)
                .map_err(|e| format!("Invalid JSON document: {e}"))?;
            d.normalize_runs();
            d
        }
        other => match efizzy_formats::import_on(other, bytes, page) {
            Some(Ok(mut d)) => {
                fit_pictures(&mut d);
                d
            }
            Some(Err(e)) => return Err(e),
            None => return open_by_content(&clean_ext, bytes, password, page),
        },
    };

    Ok((doc, false))
}

fn signature_contradicts(ext: &str, bytes: &[u8]) -> bool {
    let expected = match ext {
        "docx" | "docm" | "dotx" | "dotm" => "zip",
        "doc" | "dot" => "ole",
        "rtf" => "rtf",
        _ => return false,
    };
    let held = if bytes.starts_with(ZIP_SIG) {
        "zip"
    } else if bytes.starts_with(OLE_SIG) {
        "ole"
    } else if bytes.starts_with(b"{\\rtf") {
        "rtf"
    } else if is_word_xml(bytes) {
        "xml"
    } else if looks_like_web_page(bytes) {
        "html"
    } else {
        return false;
    };
    held != expected
}

fn is_word_xml(bytes: &[u8]) -> bool {
    let utf16 = bytes.starts_with(&[0xFF, 0xFE]) || bytes.starts_with(&[0xFE, 0xFF]);
    let after_bom = {
        let b = bytes.strip_prefix(&[0xEF, 0xBB, 0xBF][..]).unwrap_or(bytes);
        let start = b.iter().position(|c| !c.is_ascii_whitespace()).unwrap_or(b.len());
        b.get(start..).unwrap_or_default()
    };
    (utf16 || after_bom.starts_with(b"<"))
        && !matches!(efizzy_docx::sniff_xml(bytes), efizzy_docx::XmlKind::Other)
}

fn looks_like_web_page(bytes: &[u8]) -> bool {
    let head = efizzy_formats::txt::decode(bytes.get(..bytes.len().min(4096)).unwrap_or(bytes))
        .to_ascii_lowercase();
    let mut t = head.trim_start_matches(['\u{FEFF}', ' ', '\t', '\r', '\n']);
    for _ in 0..64 {
        let skip = if t.starts_with("<!--") {
            t.find("-->").map(|i| i + 3)
        } else if t.starts_with("<?") {
            t.find("?>").map(|i| i + 2)
        } else {
            break;
        };
        let Some(rest) = skip.and_then(|i| t.get(i..)) else { return false };
        t = rest.trim_start();
    }
    let tag = |name: &str| {
        t.strip_prefix(name).is_some_and(|rest| {
            rest.is_empty() || rest.starts_with(|c: char| c.is_ascii_whitespace() || c == '>' || c == '/')
        })
    };
    tag("<!doctype html") || tag("<html")
}

fn open_by_content(
    ext: &str,
    bytes: &[u8],
    password: Option<&str>,
    page: &efizzy_doc::SectionProps,
) -> Result<(Document, bool), String> {
    if bytes.starts_with(OLE_SIG) && efizzy_docx::is_encrypted(bytes) {
        let doc = efizzy_docx::read_with_password(bytes, password).map_err(|e| e.to_string())?;
        return Ok((doc, true));
    }
    if bytes.starts_with(ZIP_SIG) {
        let doc = efizzy_docx::read(bytes)
            .map_err(|e| format!("Failed to read ZIP document package: {e}"))?;
        return Ok((doc, false));
    }
    if bytes.starts_with(OLE_SIG) {
        return open_binary_doc(bytes, password);
    }
    if bytes.starts_with(b"{\\rtf") {
        let doc = efizzy_formats::rtf::import(bytes)?;
        return Ok((doc, false));
    }
    if is_word_xml(bytes) {
        let doc = efizzy_docx::read_xml(bytes).map_err(|e| format!("Failed to read XML document: {e}"))?;
        return Ok((doc, false));
    }
    if matches!(ext, "doc" | "dot" | "docx" | "docm" | "dotx" | "dotm" | "rtf")
        && looks_like_web_page(bytes)
    {
        if let Some(Ok(mut d)) = efizzy_formats::import_on("html", bytes, page) {
            fit_pictures(&mut d);
            return Ok((d, false));
        }
    }
    if efizzy_formats::txt::is_text(bytes) {
        return Ok((efizzy_formats::txt::import_on(bytes, page), false));
    }

    if ext.is_empty() {
        Err("Unsupported file format: not a recognizable document or text file".to_string())
    } else {
        Err(format!("Unsupported format `.{ext}`: not a recognizable document or text file"))
    }
}

fn each_para(b: &mut efizzy_doc::Block, depth: usize, f: &mut dyn FnMut(&mut efizzy_doc::Paragraph)) {
    if depth > 32 {
        return;
    }
    match b {
        efizzy_doc::Block::Para(p) => f(p),
        efizzy_doc::Block::Table(t) => {
            for row in &mut t.rows {
                for cell in &mut row.cells {
                    for cb in &mut cell.blocks {
                        each_para(std::sync::Arc::make_mut(cb), depth + 1, f);
                    }
                }
            }
        }
    }
}

fn fit_pictures(doc: &mut Document) {
    let max_w = doc
        .sections()
        .iter()
        .map(|(_, sp)| sp.text_width())
        .fold(f32::INFINITY, f32::min)
        .clamp(36.0, 4000.0);

    for b in &mut doc.body {
        let block = std::sync::Arc::make_mut(b);
        each_para(block, 0, &mut |p| {
            let mut changed = false;
            for o in &mut p.objects {
                if let efizzy_doc::InlineObject::Image { w, h, float, .. } = o {
                    if float.wrap == efizzy_doc::para::Wrap::Inline && w.is_finite() && *w > max_w {
                        let k = max_w / *w;
                        *w = max_w;
                        *h = (*h * k).max(1.0);
                        changed = true;
                    }
                }
            }
            if changed {
                p.touch();
            }
        });
    }
}

fn load_embedded_fonts(doc: &Document) {
    let files: Vec<(Vec<u8>, String)> = efizzy_docx::embedded_fonts(doc)
        .into_iter()
        .filter_map(|f| efizzy_fonts::decode_font_file(&f.data).ok().map(|data| (data, f.name)))
        .collect();
    if !files.is_empty() {
        efizzy_fonts::FontDb::global().add_document_fonts(files);
    }
}
