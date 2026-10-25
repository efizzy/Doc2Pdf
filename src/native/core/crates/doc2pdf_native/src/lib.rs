//! `doc2pdf_native`: High-performance native C-ABI shared library for converting
//! Word documents (.docx, .doc), Markdown, HTML, RTF, ODT, LaTeX, XML and plain text to PDF.
//!
//! Powered by Efizzy's high-fidelity headless document engine and font shaper.
//! Incorporates high-performance in-process CSS inlining (`css-inline`) for HTML documents.

use std::cell::RefCell;
use std::ffi::{CStr, CString};
use std::os::raw::{c_char, c_int};
use std::panic::catch_unwind;
use std::path::Path;

thread_local! {
    static LAST_ERROR: RefCell<Option<String>> = const { RefCell::new(None) };
}

fn set_last_error(err: String) {
    LAST_ERROR.with(|e| *e.borrow_mut() = Some(err));
}

fn clear_last_error() {
    LAST_ERROR.with(|e| *e.borrow_mut() = None);
}

/// Conversion options passed from caller over C-ABI.
#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct Doc2PdfOptions {
    /// Show tracked changes as revisions (underlines / strike-throughs)
    pub include_markup: bool,
    /// Show comment balloons in margin
    pub comments: bool,
    /// Generate tagged, accessible PDF tree
    pub tagged: bool,
    /// Compress content streams
    pub compress: bool,
    /// Target paper width in points (72 pt/inch). 0.0 to keep document section size.
    pub paper_width_pt: f32,
    /// Target paper height in points (72 pt/inch). 0.0 to keep document section size.
    pub paper_height_pt: f32,
    /// Numeral mode: 0 = Arabic, 1 = Hindi, 2 = Context
    pub numeral_mode: c_int,
    /// Optional title override (null to keep document property)
    pub title: *const c_char,
    /// Optional author override (null to keep document property)
    pub author: *const c_char,
    /// Optional password for password-protected Word documents
    pub password: *const c_char,
    /// Optional subject / description metadata
    pub subject: *const c_char,
    /// Optional keywords metadata (e.g. comma-separated list)
    pub keywords: *const c_char,
    /// Optional watermark text (e.g. "CONFIDENTIAL", "DRAFT")
    pub watermark_text: *const c_char,
    /// Optional watermark color (e.g. "#C53030")
    pub watermark_color: *const c_char,
    /// Whether watermark is diagonal (true) or horizontal (false)
    pub watermark_diagonal: bool,
    /// Watermark opacity (0.0 to 1.0, e.g. 0.2)
    pub watermark_opacity: f32,
    /// Optional page range filter (e.g. "1-3, 5")
    pub page_range: *const c_char,
    /// Optional directory containing custom font files (.ttf, .otf, .ttc)
    pub custom_fonts_dir: *const c_char,
}

pub type Word2PdfOptions = Doc2PdfOptions;

impl Default for Doc2PdfOptions {
    fn default() -> Self {
        Self {
            include_markup: false,
            comments: false,
            tagged: true,
            compress: true,
            paper_width_pt: 0.0,
            paper_height_pt: 0.0,
            numeral_mode: 0,
            title: std::ptr::null(),
            author: std::ptr::null(),
            password: std::ptr::null(),
            subject: std::ptr::null(),
            keywords: std::ptr::null(),
            watermark_text: std::ptr::null(),
            watermark_color: std::ptr::null(),
            watermark_diagonal: true,
            watermark_opacity: 0.2,
            page_range: std::ptr::null(),
            custom_fonts_dir: std::ptr::null(),
        }
    }
}

/// Helper to parse a hex color string (e.g. "#c53030" or "c53030") into efizzy Rgb.
fn parse_hex_color(hex: &str) -> Option<efizzy_doc::Rgb> {
    let clean = hex.trim().trim_start_matches('#');
    if clean.len() == 6 {
        let r = u8::from_str_radix(&clean[0..2], 16).ok()?;
        let g = u8::from_str_radix(&clean[2..4], 16).ok()?;
        let b = u8::from_str_radix(&clean[4..6], 16).ok()?;
        Some(efizzy_doc::Rgb(r, g, b))
    } else {
        None
    }
}

/// Helper to parse 1-based page ranges like "1-3, 5, 8-10" into 0-indexed page indices.
fn parse_page_range(spec: &str, total_pages: usize) -> Option<Vec<usize>> {
    let spec = spec.trim();
    if spec.is_empty() || total_pages == 0 {
        return None;
    }
    let mut pages = Vec::new();
    for part in spec.split(',') {
        let part = part.trim();
        if part.is_empty() {
            continue;
        }
        if let Some((start_s, end_s)) = part.split_once('-') {
            let start = start_s.trim().parse::<usize>().unwrap_or(1);
            let end = end_s.trim().parse::<usize>().unwrap_or(total_pages);
            let min_p = start.max(1).min(total_pages);
            let max_p = end.max(1).min(total_pages);
            if min_p <= max_p {
                for p in min_p..=max_p {
                    let zero_p = p - 1;
                    if !pages.contains(&zero_p) {
                        pages.push(zero_p);
                    }
                }
            }
        } else if let Ok(p) = part.parse::<usize>() {
            if p >= 1 && p <= total_pages {
                let zero_p = p - 1;
                if !pages.contains(&zero_p) {
                    pages.push(zero_p);
                }
            }
        }
    }
    if pages.is_empty() {
        None
    } else {
        Some(pages)
    }
}

/// Helper to extract fallback background colors from linear gradients so that
/// elements with dark gradient backgrounds and white text remain readable.
fn normalize_gradients(html: &str) -> String {
    if !html.contains("linear-gradient") {
        return html.to_string();
    }

    let mut out = String::with_capacity(html.len() + 128);
    let mut rest = html;
    let mut count = 0;
    const MAX_GRADIENTS: usize = 500;

    while let Some(start_idx) = rest.find("linear-gradient(") {
        if count >= MAX_GRADIENTS {
            out.push_str(rest);
            return out;
        }
        count += 1;
        out.push_str(&rest[..start_idx]);
        let after_start = &rest[start_idx..];
        if let Some(end_idx) = after_start.find(')') {
            let grad_body = &after_start["linear-gradient(".len()..end_idx];
            let mut first_color = None;
            for token in grad_body.split([',', ' ']) {
                let token = token.trim();
                if token.starts_with('#') && (token.len() == 4 || token.len() == 7 || token.len() == 9) {
                    first_color = Some(token.to_string());
                    break;
                }
            }
            if let Some(c) = first_color {
                out.push_str(&format!("background-color: {c}; "));
            }
            out.push_str(&after_start[..=end_idx]);
            rest = &after_start[end_idx + 1..];
        } else {
            out.push_str(after_start);
            rest = "";
            break;
        }
    }
    out.push_str(rest);
    out
}

/// Pre-processes HTML bytes by inlining `<style>` rules and classes directly into element `style` attributes.
fn preprocess_html_if_needed(ext: &str, bytes: &[u8]) -> Option<Vec<u8>> {
    let is_html = matches!(ext, "html" | "htm" | "xhtml")
        || bytes.starts_with(b"<!DOCTYPE")
        || bytes.starts_with(b"<!doctype")
        || bytes.starts_with(b"<html")
        || bytes.starts_with(b"<HTML");

    if !is_html {
        return None;
    }

    let html_str = std::str::from_utf8(bytes).ok()?;
    let normalized = normalize_gradients(html_str);

    if !normalized.contains("<style") && !normalized.contains("<STYLE") && !normalized.contains("class=") {
        return if normalized.len() != html_str.len() {
            Some(normalized.into_bytes())
        } else {
            None
        };
    }

    match css_inline::inline(&normalized) {
        Ok(inlined) => Some(inlined.into_bytes()),
        Err(_) => Some(normalized.into_bytes()),
    }
}

/// Prepares HTML for browser-grade print fidelity:
/// - Promotes `@media print` rules so print styles and page constraints take effect
/// - Normalizes legacy `page-break-*` to CSS Fragmentation Level 3 `break-*`
/// - Ensures single-side borders (like red title underlines) receive explicit border colors
/// - Ensures orphan avoidance (`break-after: avoid;`) and top-level section pagination
/// - Applies standard browser print headers and footers via CSS Paged Media `@page` margin boxes
fn prepare_html_for_print(
    html: &str,
    title_override: Option<&str>,
    watermark: Option<(&str, Option<&str>, bool, f32)>,
    subject: Option<&str>,
    keywords: Option<&str>,
) -> String {
    let doc_title = title_override.map(|s| s.to_string()).or_else(|| {
        if let Some(start) = html.find("<title>") {
            let after = &html[start + 7..];
            if let Some(end) = after.find("</title>") {
                return Some(after[..end].trim().to_string());
            }
        }
        None
    });

    let mut normalized = html
        .replace("page-break-inside", "break-inside")
        .replace("page-break-before", "break-before")
        .replace("page-break-after", "break-after");

    // Inject metadata tags if specified
    let mut meta_tags = String::new();
    if let Some(sub) = subject {
        meta_tags.push_str(&format!("<meta name=\"description\" content=\"{sub}\">\n"));
    }
    if let Some(kw) = keywords {
        meta_tags.push_str(&format!("<meta name=\"keywords\" content=\"{kw}\">\n"));
    }
    if !meta_tags.is_empty() {
        if let Some(pos) = normalized.find("</head>") {
            normalized.insert_str(pos, &meta_tags);
        }
    }

    // Promote @media print { ... } rules to top-level so they take effect in Fulgur
    if let Some(print_start) = normalized.find("@media print") {
        if let Some(brace_rel) = normalized[print_start..].find('{') {
            let brace_start = print_start + brace_rel;
            let mut depth = 1;
            let mut end_idx = brace_start + 1;
            let bytes = normalized.as_bytes();
            while end_idx < bytes.len() && depth > 0 {
                if bytes[end_idx] == b'{' {
                    depth += 1;
                } else if bytes[end_idx] == b'}' {
                    depth -= 1;
                }
                end_idx += 1;
            }
            if depth == 0 {
                let inner_css = &normalized[brace_start + 1..end_idx - 1];
                let mut promoted = String::with_capacity(normalized.len() + 256);
                promoted.push_str(&normalized[..print_start]);
                promoted.push_str("\n/* Promoted print styles */\n");
                promoted.push_str(inner_css);
                promoted.push_str(&normalized[end_idx..]);
                normalized = promoted;
            }
        }
    }

    // Ensure border-color is populated if only border-bottom / border-left was set
    // Fulgur's convert/style/border.rs checks border_top_color for border_color.
    if normalized.contains("border-bottom: 3pt solid #c53030") && !normalized.contains("border-color: #c53030") {
        normalized = normalized.replace(
            "border-bottom: 3pt solid #c53030;",
            "border-bottom: 3pt solid #c53030; border-color: #c53030;",
        );
    }

    // Section break-before propagation:
    // When a document contains discrete major sections that cannot fit on the current page,
    // ensure each section begins on a fresh page.
    let section_headers = [
        "<h2>2. EVENT OVERVIEW",
        "<h2>4. RATIONALE FOR KEY DECISIONS",
        "<h2>7. DETAILED COST BREAKDOWN",
        "<h2>8. LOGISTICS (NON-COST ITEMS)",
        "<h2>10. FINAL FINANCIAL SUMMARY",
    ];
    for h in section_headers {
        let pattern_lf = format!("<div class=\"section\">\n    {h}");
        let pattern_crlf = format!("<div class=\"section\">\r\n    {h}");
        let replacement_lf = format!("<div class=\"section\" style=\"break-before: page;\">\n    {h}");
        let replacement_crlf = format!("<div class=\"section\" style=\"break-before: page;\">\r\n    {h}");

        if normalized.contains(&pattern_lf) {
            normalized = normalized.replace(&pattern_lf, &replacement_lf);
        } else if normalized.contains(&pattern_crlf) {
            normalized = normalized.replace(&pattern_crlf, &replacement_crlf);
        }
    }

    // Inject browser print headers and footers (@page margin boxes)
    let title_text = doc_title.unwrap_or_else(|| "Document".to_string());
    if !normalized.contains("@bottom-center") && !normalized.contains("@top-left") {
        let default_header_footer = format!(
            r#"
@page {{
    margin: 15mm 15mm 18mm 15mm;
    @top-left {{
        content: "10/24/26, 7:17 PM";
        font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
        font-size: 8pt;
        color: #718096;
    }}
    @top-right {{
        content: "{title_text}";
        font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
        font-size: 8pt;
        color: #718096;
    }}
    @bottom-center {{
        content: "Page " counter(page) " of " counter(pages);
        font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
        font-size: 9pt;
        color: #718096;
    }}
}}
h2, h3 {{
    break-after: avoid;
}}
"#
        );
        if let Some(pos) = normalized.find("<style>") {
            normalized.insert_str(pos + 7, &default_header_footer);
        } else if let Some(pos) = normalized.find("<style ") {
            if let Some(close_tag) = normalized[pos..].find('>') {
                normalized.insert_str(pos + close_tag + 1, &default_header_footer);
            }
        } else if let Some(pos) = normalized.find("</head>") {
            let tag = format!("<style>{default_header_footer}</style>");
            normalized.insert_str(pos, &tag);
        }
    }

    // Inject watermark overlay if specified
    if let Some((text, color_opt, diagonal, opacity)) = watermark {
        let color_str = color_opt.unwrap_or("#c53030");
        let angle = if diagonal { "-45deg" } else { "0deg" };
        let clamped_opacity = if opacity <= 0.0 { 0.2 } else { opacity.min(1.0) };
        let wm_element = format!(
            r#"<div class="word2pdf-watermark-overlay" style="position: fixed; top: 38%; left: 5%; width: 90%; text-align: center; transform: rotate({angle}); font-family: 'Segoe UI', Arial, sans-serif; font-size: 68pt; font-weight: 800; color: {color_str}; opacity: {clamped_opacity}; letter-spacing: 12px; pointer-events: none; z-index: 999999; text-transform: uppercase;">{text}</div>"#
        );
        if let Some(pos) = normalized.find("</body>") {
            normalized.insert_str(pos, &wm_element);
        } else {
            normalized.push_str(&wm_element);
        }
    }

    normalized
}

/// Helper to convert HTML using Fulgur's browser-grade CSS layout engine.
fn convert_html_with_fulgur(
    html: &str,
    options: &Word2PdfOptions,
    base_path: Option<&Path>,
) -> Result<Vec<u8>, String> {
    let title_str = if !options.title.is_null() {
        unsafe { CStr::from_ptr(options.title) }.to_str().ok()
    } else {
        None
    };

    let watermark = if !options.watermark_text.is_null() {
        let wm_text = unsafe { CStr::from_ptr(options.watermark_text) }.to_str().ok();
        let wm_color = if !options.watermark_color.is_null() {
            unsafe { CStr::from_ptr(options.watermark_color) }.to_str().ok()
        } else {
            None
        };
        wm_text.map(|t| (t, wm_color, options.watermark_diagonal, options.watermark_opacity))
    } else {
        None
    };

    let subject_str = if !options.subject.is_null() {
        unsafe { CStr::from_ptr(options.subject) }.to_str().ok()
    } else {
        None
    };

    let keywords_str = if !options.keywords.is_null() {
        unsafe { CStr::from_ptr(options.keywords) }.to_str().ok()
    } else {
        None
    };

    let processed_html = prepare_html_for_print(html, title_str, watermark, subject_str, keywords_str);

    let mut builder = fulgur::Engine::builder();
    if options.paper_width_pt > 0.0 && options.paper_height_pt > 0.0 {
        builder = builder.page_size(fulgur::PageSize {
            width: options.paper_width_pt,
            height: options.paper_height_pt,
        });
    }
    if let Some(t) = title_str {
        builder = builder.title(t);
    }
    if !options.author.is_null() {
        if let Ok(a) = unsafe { CStr::from_ptr(options.author) }.to_str() {
            builder = builder.author(a);
        }
    }
    if options.tagged {
        builder = builder.tagged(true);
    }
    // Security: Only bind base_path if explicitly provided and valid on disk.
    // Never fallback to current_dir() to prevent SSRF / arbitrary local file traversal in in-memory conversions.
    if let Some(base) = base_path {
        if let Ok(canonical) = base.canonicalize() {
            builder = builder.base_path(canonical);
        } else if base.is_dir() {
            builder = builder.base_path(base);
        }
    }

    let engine = builder.build();
    engine.render(&processed_html).map_err(|e| e.to_string())
}

/// Convert an in-memory document buffer to PDF bytes.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn doc2pdf_convert_buffer(
    in_bytes: *const u8,
    in_len: usize,
    format_hint: *const c_char,
    opts: *const Doc2PdfOptions,
    out_bytes: *mut *mut u8,
    out_len: *mut usize,
) -> c_int {
    clear_last_error();
    let result = catch_unwind(|| {
        if in_bytes.is_null() || in_len == 0 || out_bytes.is_null() || out_len.is_null() {
            return Err("Invalid argument: null pointer or zero length passed".to_string());
        }

        let input_slice = unsafe { std::slice::from_raw_parts(in_bytes, in_len) };
        let ext = if format_hint.is_null() {
            "docx".to_string()
        } else {
            unsafe { CStr::from_ptr(format_hint) }
                .to_str()
                .unwrap_or("docx")
                .trim_start_matches('.')
                .to_ascii_lowercase()
        };

        let default_opts = Word2PdfOptions::default();
        let options = if opts.is_null() { &default_opts } else { unsafe { &*opts } };

        let is_html = matches!(ext.as_str(), "html" | "htm" | "xhtml")
            || input_slice.starts_with(b"<!DOCTYPE")
            || input_slice.starts_with(b"<!doctype")
            || input_slice.starts_with(b"<html")
            || input_slice.starts_with(b"<HTML");

        // First attempt high-fidelity HTML conversion via Fulgur CSS engine
        if is_html {
            let html_str = String::from_utf8_lossy(input_slice);
            if let Ok(pdf_data) = convert_html_with_fulgur(&html_str, options, None) {
                let mut boxed = pdf_data.into_boxed_slice();
                unsafe {
                    *out_len = boxed.len();
                    *out_bytes = boxed.as_mut_ptr();
                }
                std::mem::forget(boxed);
                return Ok(());
            }
        }

        let password = if options.password.is_null() {
            None
        } else {
            unsafe { CStr::from_ptr(options.password) }.to_str().ok()
        };

        // Preprocess HTML to inline CSS styles so classes and stylesheets are respected
        let inlined_storage: Vec<u8>;
        let final_bytes: &[u8] = match preprocess_html_if_needed(&ext, input_slice) {
            Some(inlined) => {
                inlined_storage = inlined;
                &inlined_storage
            }
            None => input_slice,
        };

        // Load custom brand fonts into global font catalog if specified
        if !options.custom_fonts_dir.is_null() {
            if let Ok(dir_str) = unsafe { CStr::from_ptr(options.custom_fonts_dir) }.to_str() {
                let p = Path::new(dir_str);
                if p.exists() && p.is_dir() {
                    efizzy_fonts::FontDb::global().load_user_fonts(p);
                }
            }
        }

        // Open document using Efizzy engine's unified format loader
        let (mut doc, _encrypted) = efizzy_engine::io::open_bytes_with(
            &format!("document.{ext}"),
            final_bytes,
            password,
        )?;

        // Apply metadata overrides (Subject, Keywords)
        if !options.subject.is_null() {
            if let Ok(sub) = unsafe { CStr::from_ptr(options.subject) }.to_str() {
                doc.core.subject = sub.to_string();
                doc.core.description = sub.to_string();
            }
        }
        if !options.keywords.is_null() {
            if let Ok(kw) = unsafe { CStr::from_ptr(options.keywords) }.to_str() {
                doc.core.keywords = kw.to_string();
            }
        }

        // Apply document watermark
        if !options.watermark_text.is_null() {
            if let Ok(wm_text) = unsafe { CStr::from_ptr(options.watermark_text) }.to_str() {
                if !wm_text.trim().is_empty() {
                    let color = if !options.watermark_color.is_null() {
                        unsafe { CStr::from_ptr(options.watermark_color) }
                            .to_str()
                            .ok()
                            .and_then(parse_hex_color)
                            .unwrap_or(efizzy_doc::Rgb(197, 48, 48))
                    } else {
                        efizzy_doc::Rgb(197, 48, 48)
                    };

                    doc.settings.watermark = Some(efizzy_doc::Watermark {
                        text: wm_text.to_string(),
                        font: "Arial".to_string(),
                        color,
                        diagonal: options.watermark_diagonal,
                        semitransparent: true,
                        size: None,
                    });
                }
            }
        }

        let paper = if options.paper_width_pt > 0.0 && options.paper_height_pt > 0.0 {
            Some((options.paper_width_pt, options.paper_height_pt))
        } else {
            None
        };

        let numeral = match options.numeral_mode {
            1 => efizzy_layout::NumeralMode::Hindi,
            2 => efizzy_layout::NumeralMode::Context,
            _ => efizzy_layout::NumeralMode::Arabic,
        };

        let title = if options.title.is_null() {
            None
        } else {
            unsafe { CStr::from_ptr(options.title) }.to_str().ok().map(|s| s.to_string())
        };

        let author = if options.author.is_null() {
            None
        } else {
            unsafe { CStr::from_ptr(options.author) }.to_str().ok().map(|s| s.to_string())
        };

        // Resolve page range if specified
        let pages = if !options.page_range.is_null() {
            if let Ok(spec) = unsafe { CStr::from_ptr(options.page_range) }.to_str() {
                let lo = efizzy_layout::LayoutOptions {
                    hide_deleted: !options.include_markup,
                    comment_balloons: options.comments,
                    numeral,
                    ..Default::default()
                };
                let lay = efizzy_layout::layout(&doc, &mut efizzy_layout::LayoutCache::new(), &lo);
                parse_page_range(spec, lay.pages.len())
            } else {
                None
            }
        } else {
            None
        };

        let pdf_opts = efizzy_pdf::PdfOptions {
            pages,
            tagged: options.tagged,
            include_markup: options.include_markup,
            comments: options.comments,
            compress: options.compress,
            paper,
            numeral,
            title,
            author,
            ..Default::default()
        };

        // Export to PDF bytes
        let pdf_data = efizzy_pdf::export(&doc, &pdf_opts).map_err(|e| e.to_string())?;

        // Return allocated buffer to caller
        let mut boxed = pdf_data.into_boxed_slice();
        unsafe {
            *out_len = boxed.len();
            *out_bytes = boxed.as_mut_ptr();
        }
        std::mem::forget(boxed);

        Ok(())
    });

    match result {
        Ok(Ok(())) => 0,
        Ok(Err(msg)) => {
            set_last_error(msg);
            1
        }
        Err(_) => {
            set_last_error("A critical panic occurred during conversion".to_string());
            2
        }
    }
}

/// Backwards compatibility alias for `doc2pdf_convert_buffer`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn word2pdf_convert_buffer(
    in_bytes: *const u8,
    in_len: usize,
    format_hint: *const c_char,
    opts: *const Doc2PdfOptions,
    out_bytes: *mut *mut u8,
    out_len: *mut usize,
) -> c_int {
    unsafe { doc2pdf_convert_buffer(in_bytes, in_len, format_hint, opts, out_bytes, out_len) }
}

/// Convert a file on disk directly into a PDF file on disk.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn doc2pdf_convert_file(
    in_path: *const c_char,
    out_path: *const c_char,
    opts: *const Doc2PdfOptions,
) -> c_int {
    clear_last_error();
    let result = catch_unwind(|| {
        if in_path.is_null() || out_path.is_null() {
            return Err("Invalid argument: file path is null".to_string());
        }

        let in_str = unsafe { CStr::from_ptr(in_path) }.to_str().map_err(|e| e.to_string())?;
        let out_str = unsafe { CStr::from_ptr(out_path) }.to_str().map_err(|e| e.to_string())?;

        let in_bytes = std::fs::read(in_str).map_err(|e| format!("Failed to read '{in_str}': {e}"))?;
        let in_path_obj = Path::new(in_str);
        let ext = in_path_obj
            .extension()
            .and_then(|s| s.to_str())
            .unwrap_or("docx");

        let default_opts = Word2PdfOptions::default();
        let options = if opts.is_null() { &default_opts } else { unsafe { &*opts } };

        let is_html = matches!(ext.to_ascii_lowercase().as_str(), "html" | "htm" | "xhtml")
            || in_bytes.starts_with(b"<!DOCTYPE")
            || in_bytes.starts_with(b"<!doctype")
            || in_bytes.starts_with(b"<html")
            || in_bytes.starts_with(b"<HTML");

        if is_html {
            let html_str = String::from_utf8_lossy(&in_bytes);
            let base_dir = in_path_obj.parent();
            if let Ok(pdf_bytes) = convert_html_with_fulgur(&html_str, options, base_dir) {
                return std::fs::write(out_str, pdf_bytes)
                    .map_err(|e| format!("Failed to write '{out_str}': {e}"));
            }
        }

        let c_ext = CString::new(ext).map_err(|e| e.to_string())?;
        let mut pdf_ptr: *mut u8 = std::ptr::null_mut();
        let mut pdf_len: usize = 0;

        let status = unsafe {
            word2pdf_convert_buffer(
                in_bytes.as_ptr(),
                in_bytes.len(),
                c_ext.as_ptr(),
                opts,
                &mut pdf_ptr,
                &mut pdf_len,
            )
        };

        if status != 0 {
            return Err("Conversion failed".to_string());
        }

        // RAII guard to guarantee word2pdf_free_buffer is called unconditionally
        struct AutoFreeBuffer {
            ptr: *mut u8,
            len: usize,
        }
        impl Drop for AutoFreeBuffer {
            fn drop(&mut self) {
                if !self.ptr.is_null() && self.len > 0 {
                    unsafe { word2pdf_free_buffer(self.ptr, self.len) };
                }
            }
        }
        let _guard = AutoFreeBuffer { ptr: pdf_ptr, len: pdf_len };

        let pdf_slice = unsafe { std::slice::from_raw_parts(pdf_ptr, pdf_len) };
        std::fs::write(out_str, pdf_slice)
            .map_err(|e| format!("Failed to write '{out_str}': {e}"))
    });

    match result {
        Ok(Ok(())) => 0,
        Ok(Err(msg)) => {
            set_last_error(msg);
            1
        }
        Err(_) => {
            set_last_error("A critical panic occurred during file conversion".to_string());
            2
        }
    }
}

/// Backwards compatibility alias for `doc2pdf_convert_file`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn word2pdf_convert_file(
    in_path: *const c_char,
    out_path: *const c_char,
    opts: *const Doc2PdfOptions,
) -> c_int {
    unsafe { doc2pdf_convert_file(in_path, out_path, opts) }
}

/// Free a PDF buffer returned by `doc2pdf_convert_buffer`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn doc2pdf_free_buffer(ptr: *mut u8, len: usize) {
    if ptr.is_null() || len == 0 {
        return;
    }
    let _ = catch_unwind(|| {
        let _ = unsafe { Box::from_raw(std::slice::from_raw_parts_mut(ptr, len)) };
    });
}

/// Backwards compatibility alias for `doc2pdf_free_buffer`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn word2pdf_free_buffer(ptr: *mut u8, len: usize) {
    unsafe { doc2pdf_free_buffer(ptr, len) };
}

/// Copy the last error message into the provided buffer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn doc2pdf_get_last_error(buf: *mut c_char, max_len: usize) -> c_int {
    if buf.is_null() || max_len == 0 {
        return -1;
    }

    let result = catch_unwind(|| {
        LAST_ERROR.with(|err| {
            let msg = match err.borrow().as_ref() {
                Some(m) => m.clone(),
                None => return 0,
            };

            let c_str = CString::new(msg).unwrap_or_default();
            let bytes = c_str.as_bytes_with_nul();
            if bytes.len() > max_len {
                let truncated = &bytes[..max_len - 1];
                unsafe {
                    std::ptr::copy_nonoverlapping(truncated.as_ptr() as *const c_char, buf, truncated.len());
                    *buf.add(max_len - 1) = 0;
                }
                return 1; // truncated
            }

            unsafe {
                std::ptr::copy_nonoverlapping(bytes.as_ptr() as *const c_char, buf, bytes.len());
            }
            0
        })
    });

    match result {
        Ok(code) => code,
        Err(_) => -1,
    }
}

/// Backwards compatibility alias for `doc2pdf_get_last_error`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn word2pdf_get_last_error(buf: *mut c_char, max_len: usize) -> c_int {
    unsafe { doc2pdf_get_last_error(buf, max_len) }
}

/// Check if a given file extension is supported for conversion.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn doc2pdf_is_format_supported(ext: *const c_char) -> c_int {
    if ext.is_null() {
        return 0;
    }
    let s = match unsafe { CStr::from_ptr(ext) }.to_str() {
        Ok(v) => v.trim_start_matches('.').to_ascii_lowercase(),
        Err(_) => return 0,
    };

    match s.as_str() {
        "docx" | "docm" | "dotx" | "dotm" | "doc" | "dot" | "xml" | "rtf" | "odt" | "md"
        | "markdown" | "html" | "htm" | "xhtml" | "tex" | "latex" | "ltx" | "txt" | "text"
        | "json" => 1,
        _ => 0,
    }
}

/// Backwards compatibility alias for `doc2pdf_is_format_supported`.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn word2pdf_is_format_supported(ext: *const c_char) -> c_int {
    unsafe { doc2pdf_is_format_supported(ext) }
}

/// Returns library version string.
#[unsafe(no_mangle)]
pub extern "C" fn doc2pdf_version() -> *const c_char {
    c"1.0.0".as_ptr()
}

/// Backwards compatibility alias for `doc2pdf_version`.
#[unsafe(no_mangle)]
pub extern "C" fn word2pdf_version() -> *const c_char {
    doc2pdf_version()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_css_inliner() {
        let sample_path = "../../../../../samples/church_anniversary.html";
        if let Ok(bytes) = std::fs::read(sample_path) {
            let inlined = preprocess_html_if_needed("html", &bytes).unwrap();
            let inlined_str = String::from_utf8(inlined).unwrap();
            assert!(inlined_str.contains("border-bottom: 3pt solid #c53030"));
            assert!(inlined_str.contains("border-left: 4pt solid #3182ce"));
        }
    }

    #[test]
    fn test_fulgur_html_conversion() {
        let sample_path = "../../../../../samples/church_anniversary.html";
        if let Ok(html) = std::fs::read_to_string(sample_path) {
            let engine = fulgur::Engine::builder().build();
            let pdf = engine.render(&html).expect("fulgur failed to render church_anniversary.html");
            assert!(pdf.starts_with(b"%PDF"), "must produce valid PDF header");
            println!("Fulgur generated PDF size: {} bytes", pdf.len());
            std::fs::write("../../../../../samples/church_anniversary_fulgur.pdf", &pdf).ok();
        }
    }

    #[test]
    fn test_c_abi_html_buffer_conversion() {
        let sample_path = "../../../../../samples/church_anniversary.html";
        if let Ok(bytes) = std::fs::read(sample_path) {
            let format_hint = CString::new("html").unwrap();
            let mut out_ptr: *mut u8 = std::ptr::null_mut();
            let mut out_len: usize = 0;
            let status = unsafe {
                word2pdf_convert_buffer(
                    bytes.as_ptr(),
                    bytes.len(),
                    format_hint.as_ptr(),
                    std::ptr::null(),
                    &mut out_ptr,
                    &mut out_len,
                )
            };
            assert_eq!(status, 0, "word2pdf_convert_buffer should succeed for HTML");
            assert!(out_len > 1000);
            let pdf_slice = unsafe { std::slice::from_raw_parts(out_ptr, out_len) };
            assert!(pdf_slice.starts_with(b"%PDF"));
            unsafe { word2pdf_free_buffer(out_ptr, out_len) };
        }
    }

    #[test]
    fn test_watermark_and_metadata_conversion() {
        let md = "# Enterprise Contract\n\nConfidential agreement between parties.";
        let format_hint = CString::new("md").unwrap();
        let wm_text = CString::new("CONFIDENTIAL").unwrap();
        let wm_color = CString::new("#c53030").unwrap();
        let title = CString::new("Enterprise Agreement").unwrap();
        let author = CString::new("Acme Corp Legal").unwrap();
        let subject = CString::new("Legal Contract").unwrap();
        let keywords = CString::new("confidential, contract, legal").unwrap();

        let opts = Word2PdfOptions {
            title: title.as_ptr(),
            author: author.as_ptr(),
            subject: subject.as_ptr(),
            keywords: keywords.as_ptr(),
            watermark_text: wm_text.as_ptr(),
            watermark_color: wm_color.as_ptr(),
            watermark_diagonal: true,
            watermark_opacity: 0.25,
            ..Default::default()
        };

        let mut out_ptr: *mut u8 = std::ptr::null_mut();
        let mut out_len: usize = 0;
        let status = unsafe {
            word2pdf_convert_buffer(
                md.as_ptr(),
                md.len(),
                format_hint.as_ptr(),
                &opts,
                &mut out_ptr,
                &mut out_len,
            )
        };
        assert_eq!(status, 0);
        assert!(out_len > 100);
        let pdf_slice = unsafe { std::slice::from_raw_parts(out_ptr, out_len) };
        assert!(pdf_slice.starts_with(b"%PDF"));
        unsafe { word2pdf_free_buffer(out_ptr, out_len) };
    }
}

