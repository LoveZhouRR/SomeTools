using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;

namespace DBC.WeChat.Services.Components.Picture
{
    public static class Extensions
    {
        public static Image Normalize(this Image me, Size size)
        {
            return Normalize(me, size, Color.White);
        }

        public static Image Normalize(this Image me, Size size, Color backgroundColor)
        {
            var widthRatio = (decimal)me.Size.Width / size.Width;
            var heightRatio = (decimal)me.Size.Height / size.Height;
            decimal ratio;
            if (widthRatio > 1 || heightRatio > 1)
            {
                ratio = Math.Max(widthRatio, heightRatio);
            }
            else
            {
                ratio = Math.Max(widthRatio, heightRatio);
            }

            var thumbnailSize = new Size(
                (int)(me.Size.Width / ratio),
                (int)(me.Size.Height / ratio)
            );

            //if (thumbnailSize == me.Size) return me;

            var scaled = new Bitmap(size.Width, size.Height);
            using (var grah = Graphics.FromImage(scaled))
            {
                var x = Math.Abs((thumbnailSize.Width - scaled.Width) / 2);
                var y = Math.Abs((thumbnailSize.Height - scaled.Height) / 2);

                using (var backgroundBrush = new SolidBrush(backgroundColor))
                {
                    grah.FillRectangle(backgroundBrush, 0, 0, scaled.Width, scaled.Height);
                }

                grah.DrawImage(me, new Rectangle(x, y, thumbnailSize.Width, thumbnailSize.Height));

                return scaled;
            }
        }


    }
    public static class ImageExtensions
    {
        public static byte[] ToBytes(this Image image)
        {
            using (var stream = new MemoryStream())
            {
                image.Save(stream, ImageFormat.Jpeg);

                return stream.ToArray();
            }
        }
    }
}
