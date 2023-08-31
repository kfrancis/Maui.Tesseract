namespace Maui.Tesseract;

// All the code in this file is included in all platforms.
public enum OcrEngineMode
{
    /// <summary>
    /// Run Tesseract only - fastest
    /// </summary>
    TesseractOnly = 0,
    /// <summary>
    /// Run Cube only - better accuracy, but slower
    /// </summary>
    CubeOnly = 1,
    /// <summary>
    /// Run both and combine results - best accuracy
    /// </summary>
    TesseractCubeCombined = 2,
    Default = 3
}
