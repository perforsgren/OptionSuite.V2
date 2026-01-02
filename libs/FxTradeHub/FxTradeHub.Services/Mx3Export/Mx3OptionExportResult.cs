namespace FxTradeHub.Services.Mx3Export
{
    /// <summary>
    /// D4.2a: Resultat från MX3 option XML-export.
    /// </summary>
    public sealed class Mx3OptionExportResult
    {
        /// <summary>
        /// True om XML-filen skapades utan exception.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Filnamn (utan path) för den skapade XML-filen, t.ex. "20250102_123456.xml".
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Fullständig path till den skapade XML-filen.
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Felmeddelande om Success = false.
        /// </summary>
        public string ErrorMessage { get; set; }
    }
}
