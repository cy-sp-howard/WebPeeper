function webPeeper_getFocusLocation() {
  const rect = document.activeElement.getBoundingClientRect();
  let y = rect.y + rect.height;
  let x = rect.x;
  if (y < 0) y = 0;
  else if (y > innerHeight) y = innerHeight;
  if (x < 0) x = 0;
  else if (x > innerWidth) x = innerWidth;
  return [Math.round(x * devicePixelRatio), Math.round(y * devicePixelRatio)];
}

function webPeeper_blur() {
  const target = document.activeElement
  if (target.isContentEditable || ["INPUT", "TEXTAREA"].includes(target.tagName)) {
    webPeeper_blur.blurredElement = target;
    target.blur();
  }
}

function webPeeper_focusBlurredElement() {
  if (webPeeper_blur.blurredElement) {
    webPeeper_blur.blurredElement.focus();
    webPeeper_blur.blurredElement = null;
  }
}

function webPeeper_presetCSS() {
  const css = document.createElement("link");
  css.setAttribute("type", "text/css");
  css.setAttribute("rel", "stylesheet");
  css.setAttribute("href", "blish-hud://web-peeper/scrollbar.css");
  try {
    document.head.appendChild(css);
  } catch (error) {
    addEventListener(
      "DOMContentLoaded",
      () => {
        document.head.appendChild(css);
      },
      { once: true }
    );
  }
}

webPeeper_presetCSS();