using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Blish_HUD.Controls
{
    internal class IconButton : Control
    {
        readonly Texture2D _iconTexture;
        Rectangle _destIconRect;
        Rectangle _destIconHoveredRect;
        Rectangle _sourceIconRect;
        Vector2 _originPos;
        public float IconRotation = 0f;
        readonly int _sourceSize;
        public IconButton(Texture2D iconTexture, int buttonSize) : this(iconTexture, buttonSize, 0) { }
        public IconButton(Texture2D iconTexture, int buttonSize, int iconPadding) : this(iconTexture, buttonSize, iconPadding, 1.1f) { }
        public IconButton(Texture2D iconTexture, int buttonSize, float hoverScale) : this(iconTexture, buttonSize, 0, hoverScale) { }
        public IconButton(Texture2D iconTexture, int buttonSize, int iconPadding, float hoverScale)
        {
            _iconTexture = iconTexture;
            var textureResultW = buttonSize - 2 * iconPadding;
            var textureResultH = buttonSize - 2 * iconPadding;
            var textureResultHoverdW = (int)(textureResultW * hoverScale);
            var textureResultHoverdH = (int)(textureResultH * hoverScale);
            var iconPaddingHoverdX = iconPadding - (textureResultHoverdW - textureResultW) / 2;
            var iconPaddingHoverdY = iconPadding - (textureResultHoverdH - textureResultH) / 2;

            _sourceSize = MathHelper.Max(_iconTexture.Width, _iconTexture.Height);
            _destIconRect = new Rectangle(iconPadding + textureResultW / 2, iconPadding + textureResultH / 2, textureResultW, textureResultH);
            _destIconHoveredRect = new Rectangle(iconPaddingHoverdX + textureResultHoverdW / 2, iconPaddingHoverdY + textureResultHoverdH / 2, textureResultHoverdW, textureResultHoverdH);
            _sourceIconRect = new(0, 0, _sourceSize, _sourceSize);
            _originPos = new(_iconTexture.Width / 2, _iconTexture.Height / 2);
            Size = new Point(buttonSize, buttonSize);
        }
        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
        {
            spriteBatch.DrawOnCtrl(this, _iconTexture, MouseOver ? _destIconHoveredRect : _destIconRect, _sourceIconRect, Color.White, IconRotation, _originPos);
        }

    }
}
