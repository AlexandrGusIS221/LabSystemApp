using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using ZXing;

namespace Barcode
{
    public class Barcode
    {
        public static Bitmap GenerateBarcode(string code, int Width=400, int Height=200)
        {
            BarcodeWriter writer = new BarcodeWriter
            {
                Format = BarcodeFormat.CODE_128,
                Options = new ZXing.Common.EncodingOptions
                {
                    Height = Height,
                    Width = Width,
                    Margin = 10
                }
            };

            return writer.Write(code);
        }

        public static BitmapImage BarcodeToBitmapImage(Bitmap barcode)
        {
            BitmapImage bitmapImage = new BitmapImage();

            using (var memory = new MemoryStream())
            {
                barcode.Save(memory, ImageFormat.Bmp);
                memory.Position = 0;
                
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
            }

            return bitmapImage;
        }

        public static void SaveBarcodeToPdf(Bitmap barcode, string text, string filePath)
        {
            using (Document document = new Document())
            {
                using (PdfWriter writer = PdfWriter.GetInstance(document, new FileStream(filePath, FileMode.Create)))
                {
                    document.Open();

                    Paragraph paragraph = new Paragraph();
                    paragraph.Alignment = Element.ALIGN_CENTER;
                    paragraph.Add(new Chunk(text));
                    document.Add(paragraph);

                    iTextSharp.text.Image barcodeImage = iTextSharp.text.Image.GetInstance(barcode, ImageFormat.Bmp);
                    barcodeImage.Alignment = Element.ALIGN_CENTER;
                    document.Add(barcodeImage);

                    document.Close();
                }
            }
        }

        public static string GenerateRandomCode(DateTime now, int orderId)
        {
            Random random = new Random();
            const string chars = "0123456789";
            return orderId + now.ToString("ddMMyyyy") + new string(System.Linq.Enumerable.Repeat(chars, 6)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }

    public class ImageRenderListener : IRenderListener
    {
        public Bitmap Image { get; private set; }

        public ImageRenderListener(Bitmap image)
        {
            Image = image;
        }

        public void RenderImage(ImageRenderInfo renderInfo)
        {
            Matrix imageMatrix = renderInfo.GetImageCTM();
            var image = renderInfo.GetImage();
            
            if (image == null)
                return;
            
            byte[] imageBytes = image.GetImageAsBytes();
            
            if (imageBytes == null)
                return;
            
            using (MemoryStream ms = new MemoryStream(imageBytes))
            {
                using (System.Drawing.Image img = System.Drawing.Image.FromStream(ms))
                {
                    using (Graphics graphics = Graphics.FromImage(Image))
                    {
                        graphics.DrawImage(img, imageMatrix[Matrix.I31], imageMatrix[Matrix.I32], img.Width, img.Height);
                    }
                }
            }
        }

        public void RenderText(TextRenderInfo renderInfo) { }
        public void BeginTextBlock() { }
        public void EndTextBlock() { }
    }
}