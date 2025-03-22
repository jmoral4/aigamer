using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Tesseract; // Install via NuGet: Tesseract

namespace AIGamer;
public class WindowOCRReader
{
    // P/Invoke declarations
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int nFlags);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Captures the image of the specified window.
    /// </summary>
    public static Bitmap CaptureWindow(IntPtr hWnd)
    {
        if (!GetWindowRect(hWnd, out RECT rect))
            throw new Exception("Failed to get window rectangle.");

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using (Graphics graphics = Graphics.FromImage(bmp))
        {
            IntPtr hdc = graphics.GetHdc();
            bool success = PrintWindow(hWnd, hdc, 0);
            graphics.ReleaseHdc(hdc);

            if (!success)
                throw new Exception("Failed to capture window image.");
        }
        return bmp;
    }

    /// <summary>
    /// Uses Tesseract OCR to extract text from a window’s image.
    /// </summary>
    public static string GetTextFromWindow(IntPtr hWnd)
    {
        Bitmap windowImage = WindowCapture.CaptureWindowUsingCopyFromScreen(hWnd);
        try
        {
            // Adjust tessdata path as needed; ensure the folder with trained data is available.
            using (var engine = new TesseractEngine(@"tessdata", "eng", EngineMode.TesseractAndLstm))
            {
                engine.SetVariable("tessedit_pageseg_mode", "3");
                // Use a more comprehensive whitelist similar to the Python version
                string consoleChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" +
                                      "?.:_-!()# \r\n\t";
                engine.SetVariable("tessedit_char_whitelist", consoleChars);
                //engine.SetVariable("tessedit_char_whitelist", "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz )_\r\n");
                engine.SetVariable("classify_bln_numeric_mode", "1");
                //engine.SetVariable("textord_min_linesize", "1.5");
                engine.SetVariable("textord_min_xheight", "15");
                engine.SetVariable("user_defined_dpi", "300");
                engine.SetVariable("image_width", windowImage.Width.ToString());
                engine.SetVariable("image_height", windowImage.Height.ToString());
                engine.SetVariable("tessedit_ocr_engine_mode", "2");


                //engine.SetVariable("textord_heavy_nr", "1");
                windowImage = PreprocessImage(windowImage);
                //windowImage = ResizeImage(windowImage, 2);
                windowImage = InvertImage(windowImage);
                windowImage.Save($"ocr_debug_{DateTime.Now:yyyyMMddHHmmss}.png");

                using (var ms = new MemoryStream())
                {
                    // Save the Bitmap as PNG to a memory stream.
                    windowImage.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    byte[] imageBytes = ms.ToArray();

                    // Load the image from the byte array.
                    using (var pix = Pix.LoadFromMemory(imageBytes))
                    {
                        using (var page = engine.Process(pix))
                        {
                            return page.GetText();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return $"OCR error: {ex.Message}";
        }
        finally
        {
            windowImage.Dispose();
        }
    }


    public static Bitmap ResizeImage(Bitmap original, int scaleFactor)
    {
        int newWidth = original.Width * scaleFactor;
        int newHeight = original.Height * scaleFactor;
        Bitmap resized = new Bitmap(newWidth, newHeight);
        using (Graphics g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(original, 0, 0, newWidth, newHeight);
        }
        return resized;
    }

    public static Bitmap InvertImage(Bitmap original)
    {
        Bitmap inverted = new Bitmap(original.Width, original.Height);
        for (int x = 0; x < original.Width; x++)
        {
            for (int y = 0; y < original.Height; y++)
            {
                Color pixel = original.GetPixel(x, y);
                inverted.SetPixel(x, y, Color.FromArgb(255 - pixel.R, 255 - pixel.G, 255 - pixel.B));
            }
        }
        return inverted;
    }


    public static Bitmap PreprocessImage(Bitmap original)
    {
        // Convert to grayscale.
        Bitmap grayImage = new Bitmap(original.Width, original.Height);
        using (Graphics g = Graphics.FromImage(grayImage))
        {
            var colorMatrix = new ColorMatrix(new float[][]
            {
                new float[] {0.3f, 0.3f, 0.3f, 0, 0},
                new float[] {0.59f, 0.59f, 0.59f, 0, 0},
                new float[] {0.11f, 0.11f, 0.11f, 0, 0},
                new float[] {0, 0, 0, 1, 0},
                new float[] {0, 0, 0, 0, 1}
            });
            var attributes = new ImageAttributes();
            attributes.SetColorMatrix(colorMatrix);
            g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
        }

        // Apply thresholding to convert the image to black and white.
        Bitmap thresholdImage = new Bitmap(grayImage.Width, grayImage.Height);
        for (int x = 0; x < grayImage.Width; x++)
        {
            for (int y = 0; y < grayImage.Height; y++)
            {
                Color pixelColor = grayImage.GetPixel(x, y);
                // Simple threshold: adjust 128 as needed.
                Color newColor = pixelColor.R < 128 ? Color.Black : Color.White;
                thresholdImage.SetPixel(x, y, newColor);
            }
        }
        grayImage.Dispose();
        return thresholdImage;
    }

}


public class WindowCapture
{
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static Bitmap CaptureWindowUsingCopyFromScreen(IntPtr hWnd)
    {
        if (!GetWindowRect(hWnd, out RECT rect))
            throw new Exception("Failed to get window rect.");

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using (Graphics g = Graphics.FromImage(bmp))
        {
            // Capture the screen area corresponding to the window.
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
        }
        return bmp;
    }
}
