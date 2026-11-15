namespace Efizzy.Doc2Pdf;

/// <summary>
/// Specifies the cryptographic certification access permissions (DocMDP) applied to a digitally signed PDF.
/// </summary>
public enum PdfCertificationLevel
{
    /// <summary>
    /// Highest security: No changes of any kind are permitted. Any modification invalidates the document.
    /// </summary>
    NoChangesPermitted = 1,

    /// <summary>
    /// Permitted actions: User may fill form fields and sign existing signature fields.
    /// </summary>
    FormFillingOnly = 2,

    /// <summary>
    /// Permitted actions: User may fill form fields and add annotations/comments.
    /// </summary>
    FormFillingAndAnnotations = 3
}
