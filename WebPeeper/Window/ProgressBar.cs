using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;
using System;

namespace BhModule.WebPeeper.Window
{
    internal class ProgressBar(Func<float> getProgress) : Control
    {
        public string Text { get => _text; set { _text = value; RecalculateLayout(); } }
        string _text = "";
        string _wrapText = "";
        float _percentage = getProgress();
        readonly Func<float> _getProgress = getProgress;
        readonly BitmapFont _textSize = Content.DefaultFont16;
        Rectangle _textRect = Rectangle.Empty;
        Rectangle _barRect = Rectangle.Empty;
        Rectangle _barBorderRect = Rectangle.Empty;
        public Point BarSize { get => _barSize; set { _barSize = value; RecalculateLayout(); } }
        Point _barSize = Point.Zero;
        public event EventHandler<ValueChangedEventArgs<float>> ProgressUpdated;
        protected override void DisposeControl()
        {
            ProgressUpdated = null;
            base.DisposeControl();
        }
        public override void DoUpdate(GameTime gameTime)
        {
            _barRect.Width = GetBarWidth();
        }
        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (!string.IsNullOrWhiteSpace(_text))
            {
                spriteBatch.DrawStringOnCtrl(this, _wrapText, _textSize, _textRect, Color.White);
            }
            spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, _barBorderRect, Color.Black);
            spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, _barRect, Color.White);
        }
        public override void RecalculateLayout()
        {
            _barBorderRect.Size = _barSize == Point.Zero ? Size : _barSize;
            _barBorderRect.X = (int)((Width - _barBorderRect.Width) / 2);
            _barBorderRect.Y = (Height - _barBorderRect.Height) / 2;
            if (!string.IsNullOrWhiteSpace(_text))
            {
                _wrapText = DrawUtil.WrapText(_textSize, _text, Width);
                var textSize = _textSize.MeasureString(_wrapText);
                _textRect.X = (int)((Width - textSize.Width) / 2);
                _textRect.Y = (int)(Height / 2 - textSize.Height);
                _textRect.Width = (int)textSize.Width;
                _textRect.Height = (int)textSize.Height;

                _barBorderRect.Y = Height / 2 + 10;
            }

            _barRect.X = _barBorderRect.X + 1;
            _barRect.Y = _barBorderRect.Y + 1;
            _barRect.Height = _barBorderRect.Height - 2;
            _barRect.Width = GetBarWidth();
        }
        int GetBarWidth()
        {
            var percentage = _getProgress();
            if (percentage != _percentage)
            {
                ProgressUpdated?.Invoke(this, new(_percentage, percentage));
            }
            _percentage = percentage;
            return (int)(_percentage * (_barBorderRect.Width - 2));
        }
    }
}
