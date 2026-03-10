/**
 * overlay.js
 * Shared WebSocket logic for all overlay pages.
 * Each page calls initOverlay(handlers) where handlers is:
 *   { onMedia(data), onConnect?(), onDisconnect?() }
 */

(function () {
  const WS_PORT = location.port || 9090;
  const WS_URL  = `ws://localhost:${WS_PORT}/ws`;
  const RECONNECT_DELAY = 3000;

  let ws = null;
  let handlers = {};

  window.initOverlay = function (h) {
    handlers = h || {};
    connect();
  };

  function connect() {
    ws = new WebSocket(WS_URL);

    ws.addEventListener('open', () => {
      handlers.onConnect?.();
    });

    ws.addEventListener('message', (evt) => {
      try {
        const data = JSON.parse(evt.data);
        handlers.onMedia?.(data);
      } catch (e) {
        console.error('overlay.js: parse error', e);
      }
    });

    ws.addEventListener('close', () => {
      handlers.onDisconnect?.();
      setTimeout(connect, RECONNECT_DELAY);
    });

    ws.addEventListener('error', () => {
      ws.close();
    });
  }

  /** Apply theme config object to CSS variables on :root */
  window.applyTheme = function (theme) {
    if (!theme) return;
    const r = document.documentElement.style;

    if (theme.cover) {
      r.setProperty('--cover-size',   theme.cover.size + 'px');
      r.setProperty('--cover-shape',  theme.cover.shape === 'circle' ? '50%' :
                                      theme.cover.shape === 'rounded' ? '12px' : '0');
      r.setProperty('--rotate-speed', theme.cover.rotation_speed + 's');
      r.setProperty('--cover-anim',
        theme.cover.animation === 'rotate' ? 'spin var(--rotate-speed) linear infinite' :
        theme.cover.animation === 'pulse'  ? 'pulse 2s ease-in-out infinite' : 'none');
    }
    if (theme.title) {
      r.setProperty('--title-font',   `"${theme.title.font}", sans-serif`);
      r.setProperty('--title-size',   theme.title.size + 'px');
      r.setProperty('--title-color',  theme.title.color);
      r.setProperty('--title-shadow', theme.title.shadow ? '0 2px 8px rgba(0,0,0,0.8)' : 'none');
      r.setProperty('--title-weight', theme.title.bold ? 'bold' : 'normal');
    }
    if (theme.artist) {
      r.setProperty('--artist-font',   `"${theme.artist.font}", sans-serif`);
      r.setProperty('--artist-size',   theme.artist.size + 'px');
      r.setProperty('--artist-color',  theme.artist.color);
      r.setProperty('--artist-shadow', theme.artist.shadow ? '0 2px 8px rgba(0,0,0,0.8)' : 'none');
      r.setProperty('--artist-weight', theme.artist.bold ? 'bold' : 'normal');
    }
    if (theme.background) {
      if (theme.background.type === 'color') {
        r.setProperty('--bg', theme.background.color);
      } else if (theme.background.type === 'transparent') {
        r.setProperty('--bg', 'transparent');
      }
      // blur_cover is handled per-page in CSS
    }
  };

  /** Pause / resume the cover spin animation based on isPlaying */
  window.setPlayState = function (isPlaying) {
    const cover = document.getElementById('cover-img');
    if (!cover) return;
    cover.style.animationPlayState = isPlaying ? 'running' : 'paused';
  };

  /** Start marquee scroll on text elements if content overflows */
  window.setupMarquee = function (el) {
    if (!el) return;
    const parent = el.parentElement;
    if (!parent) return;
    if (el.scrollWidth > parent.clientWidth + 4) {
      el.classList.add('marquee');
    } else {
      el.classList.remove('marquee');
    }
  };
})();
