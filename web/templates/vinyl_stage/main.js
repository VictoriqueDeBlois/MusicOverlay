const $ = (sel) => document.querySelector(sel);

const coverImg = $('#cover-img');
const rotor = $('#rotor');
const playBtn = $('#play-btn');
const needle = $('#needle');
const defaultCover = 'images/default-cover.png';

function setCover(url) {
  coverImg.src = url || defaultCover;
}

function setPlaying(isPlaying) {
  if (rotor) rotor.style.animationPlayState = isPlaying ? 'running' : 'paused';
  needle.classList.toggle('play', !!isPlaying);
  if (playBtn) playBtn.classList.toggle('play', !!isPlaying);
}

initOverlay({
  onMedia(data) {
    applyTheme(data.theme);
    setCover(data.cover);
    setPlaying(!!data.isPlaying);
  }
});
