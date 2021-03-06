﻿using System.Drawing;
using ExtractorSharp.Core.Lib;
using ExtractorSharp.Core.Model;

namespace ExtractorSharp.Core.Draw.Paint {
    public class Canvas : IPaint,IAttractable{
        private bool _realPosition;

        public Point RealLocation {
            get {
                var location = Location;
                if (RealPosition && Tag is Sprite sprite) {
                    location = location.Add(sprite.Location);
                }
                return location;
            }
            set {
                var location = value;
                if (RealPosition && Tag is Sprite sprite) {
                    location = location.Minus(sprite.Location);
                }
                Location = location;
            }
        }

        public Point Offset { set; get; } = Point.Empty;
        public Size CanvasSize { set; get; }

        public bool RealPosition {
            set {
                _realPosition = value;
                if (!value) {
                    Location = Point.Empty;
                }
            }
            get => _realPosition;
        }

        public string Name { set; get; }
        public Bitmap Image { set; get; }
        public Rectangle Rectangle => new Rectangle(RealLocation, Size);

        public bool Contains(Point point) {
            return Rectangle.Contains(point);
        }

        public void Draw(Graphics g) {
            if (Tag != null && Image != null) {
                g.DrawImage(Image, Rectangle);
            }
        }

        public Point Location { set; get; } = Point.Empty;
        public Size Size { set; get; }
        public object Tag { set; get; }
        public bool Visible { set; get; }
        public bool Locked { set; get; }

        public int Range => 20;

        public override string ToString() {
            return
                $"{Language.Default[Name]},{Language.Default["Position"]}{RealLocation.GetString()},{Language.Default["Size"]}{Size.GetString()}";
        }
    }
}