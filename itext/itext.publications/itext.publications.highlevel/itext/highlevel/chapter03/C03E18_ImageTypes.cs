/*
* This example was written by Bruno Lowagie
* in the context of the book: iText 7 building blocks
*/
using System;
using System.Drawing;
using System.IO;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Test.Attributes;

namespace itext.publications.highlevel.itext.highlevel.chapter03 {
    /// <author>iText</author>
    [WrapToTest]
    public class C03E18_ImageTypes {
        public const String TEST1 = "../../resources/img/test/map.jp2";

        public const String TEST2 = "../../resources/img/test/butterfly.bmp";

        public const String TEST3 = "../../resources/img/test/hitchcock.png";

        public const String TEST4 = "../../resources/img/test/info.png";

        public const String TEST5 = "../../resources/img/test/hitchcock.gif";

        public const String TEST6 = "../../resources/img/test/amb.jb2";

        public const String TEST7 = "../../resources/img/test/marbles.tif";

        public const String DEST = "results/chapter03/image_types.pdf";

        /// <exception cref="System.IO.IOException"/>
        public static void Main(String[] args) {
            FileInfo file = new FileInfo(DEST);
            file.Directory.Create();
            new C03E18_ImageTypes().CreatePdf(DEST);
        }

        /// <exception cref="System.IO.IOException"/>
        public virtual void CreatePdf(String dest) {
            PdfDocument pdf = new PdfDocument(new PdfWriter(dest));
            Document document = new Document(pdf);
            // raw
            byte[] data = new byte[256 * 3];
            for (int i = 0; i < 256; i++) {
                data[i * 3] = (byte)(255 - i);
                data[i * 3 + 1] = (byte)(255 - i);
                data[i * 3 + 2] = (byte)i;
            }
            ImageData raw = ImageDataFactory.Create(256, 1, 3, 8, data, null);
            iText.Layout.Element.Image img = new iText.Layout.Element.Image(raw);
            img.ScaleAbsolute(256, 10);
            document.Add(img);
            // JPEG2000
            iText.Layout.Element.Image img1 = new iText.Layout.Element.Image(ImageDataFactory.Create(TEST1));
            document.Add(img1);
            document.Add(new AreaBreak());
            // BMP
            iText.Layout.Element.Image img2 = new iText.Layout.Element.Image(ImageDataFactory.Create(TEST2));
            img2.SetMarginBottom(10);
            document.Add(img2);
            // PNG
            iText.Layout.Element.Image img3 = new iText.Layout.Element.Image(ImageDataFactory.Create(TEST3));
            img3.SetMarginBottom(10);
            document.Add(img3);
            // Transparent PNG
            iText.Layout.Element.Image img4 = new iText.Layout.Element.Image(ImageDataFactory.Create(TEST4));
            img4.SetBorderLeft(new SolidBorder(6));
            document.Add(img4);
            // GIF
            iText.Layout.Element.Image img5 = new iText.Layout.Element.Image(ImageDataFactory.Create(TEST5));
            img5.SetBackgroundColor(Color.LIGHT_GRAY);
            document.Add(img5);
            // AWT
            //Todo: Review this test
            /*
            System.Drawing.Image awtImage = Toolkit.GetDefaultToolkit().CreateImage(TEST5);
            iText.Layout.Element.Image awt = new iText.Layout.Element.Image(ImageDataFactory.Create(awtImage, Color.YELLOW
                ));
            awt.SetMarginTop(10);
            document.Add(awt);
            */
            // JBIG2
            iText.Layout.Element.Image img6 = new iText.Layout.Element.Image(ImageDataFactory.Create(TEST6));
            document.Add(img6);
            // TIFF
            iText.Layout.Element.Image img7 = new iText.Layout.Element.Image(ImageDataFactory.Create(TEST7));
            document.Add(img7);
            document.Close();
        }
    }
}