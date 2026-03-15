const $ = (sel) => document.querySelector(sel);

const coverImg = $('#cover-img');
const coverNextImg = $('#cover-img-next');
const rotorCurrent = $('#rotor-current');
const rotorNext = $('#rotor-next');
const rotorCurrentSpin = rotorCurrent ? rotorCurrent.querySelector('.song-rotor-spin') : null;
const rotorNextSpin = rotorNext ? rotorNext.querySelector('.song-rotor-spin') : null;
const needle = $('#needle');
const defaultCover = 'images/default-cover.png';

const RAISED_ANGLE = 9;
const START_ANGLE = 31;
const END_ANGLE = 43;
const DRIFT_MS = 180000;
const SWAP_MS = 1000;
const NEEDLE_MOVE_MS = 400;

let elapsedMs = 0;
let drifting = false;
let rafId = 0;
let lastTick = 0;
let lastIsPlaying = false;
let currentTrackKey = '';

function setCover(url) {
  coverImg.src = url || defaultCover;
}

function getTrackKey(data) {
  return [data.title, data.artist, data.album, data.cover].join('||');
}

function setNeedleAngle(angle, animate) {
  if (!needle) return;
  if (animate) {
    needle.style.transition = `transform ${NEEDLE_MOVE_MS}ms ease`;
  } else {
    needle.style.transition = 'none';
  }
  needle.style.transform = `rotate(${angle}deg)`;
  if (!animate) {
    requestAnimationFrame(() => {
      needle.style.transition = `transform ${NEEDLE_MOVE_MS}ms ease`;
    });
  }
}

function angleFromElapsed() {
  const t = Math.min(1, elapsedMs / DRIFT_MS);
  return START_ANGLE + (END_ANGLE - START_ANGLE) * t;
}

function tick(now) {
  if (!drifting) return;
  elapsedMs += now - lastTick;
  lastTick = now;
  if (elapsedMs >= DRIFT_MS) {
    elapsedMs = DRIFT_MS;
  }
  setNeedleAngle(angleFromElapsed(), false);
  if (elapsedMs < DRIFT_MS) {
    rafId = requestAnimationFrame(tick);
  }
}

function startDrift() {
  if (drifting) return;
  drifting = true;
  lastTick = performance.now();
  rafId = requestAnimationFrame(tick);
}

function stopDrift() {
  drifting = false;
  if (rafId) cancelAnimationFrame(rafId);
  rafId = 0;
}

function handlePlay(isNewTrack) {
  if (isNewTrack) elapsedMs = 0;
  if (elapsedMs === 0) {
    setNeedleAngle(START_ANGLE, true);
    setTimeout(startDrift, NEEDLE_MOVE_MS);
  } else {
    setNeedleAngle(angleFromElapsed(), true);
    setTimeout(startDrift, NEEDLE_MOVE_MS);
  }
}

function handlePause() {
  stopDrift();
  setNeedleAngle(RAISED_ANGLE, true);
}

function swapDisc(nextCover) {
  if (!coverNextImg || !rotorNext || !rotorCurrent) {
    setCover(nextCover);
    return;
  }

  const spindle = document.querySelector('.song-spindle');
  const spindlePrevZ = spindle ? spindle.style.zIndex : '';
  if (spindle) spindle.style.zIndex = '0';

  coverNextImg.src = nextCover || defaultCover;
  rotorNext.classList.remove('swap-up', 'swap-down', 'swap-reset');
  rotorCurrent.classList.remove('swap-up', 'swap-down', 'swap-reset');
  rotorNext.classList.add('is-visible');
  rotorNext.style.setProperty('--swap-ms', `${SWAP_MS}ms`);
  rotorCurrent.style.setProperty('--swap-ms', `${SWAP_MS}ms`);

  rotorNext.style.zIndex = '2';
  rotorCurrent.style.zIndex = '3';
  rotorNext.style.transform = 'translateY(0)';
  rotorCurrent.style.transform = 'translateY(0)';

  void rotorNext.offsetWidth;
  rotorNext.classList.add('swap-up');
  rotorCurrent.classList.add('swap-down');

  setTimeout(() => {
    rotorNext.classList.remove('swap-up');
    rotorCurrent.classList.remove('swap-down');
    rotorNext.style.zIndex = '3';
    rotorCurrent.style.zIndex = '2';
    rotorNext.style.setProperty('--swap-from', '-50%');
    rotorCurrent.style.setProperty('--swap-from', '50%');
    void rotorNext.offsetWidth;
    rotorNext.classList.add('swap-reset');
    rotorCurrent.classList.add('swap-reset');
  }, SWAP_MS);

  setTimeout(() => {
    coverImg.src = nextCover;
    rotorNext.classList.remove('swap-reset');
    rotorCurrent.classList.remove('swap-reset');
    rotorNext.classList.remove('is-visible');
    rotorNext.style.zIndex = '2';
    rotorCurrent.style.zIndex = '3';
    if (spindle) spindle.style.zIndex = spindlePrevZ;
    coverNextImg.src = '';
  }, SWAP_MS * 2);
}

function setPlaying(isPlaying) {
  if (rotorCurrentSpin) rotorCurrentSpin.style.animationPlayState = isPlaying ? 'running' : 'paused';
  if (rotorNextSpin) rotorNextSpin.style.animationPlayState = isPlaying ? 'running' : 'paused';
}

initOverlay({
  onMedia(data) {
    applyTheme(data.theme);
    const isPlaying = !!data.isPlaying;
    const key = getTrackKey(data);
    const isNewTrack = key && key !== currentTrackKey;
    if (isNewTrack) {
      currentTrackKey = key;
      elapsedMs = 0;
    }

    const nextCover = data.cover || defaultCover;

    if (isNewTrack) {
      setPlaying(false);
      handlePause();
      swapDisc(nextCover);
      if (isPlaying) {
        setTimeout(() => {
          setPlaying(true);
          handlePlay(true);
        }, SWAP_MS * 2);
      } else {
        setTimeout(() => {
          setCover(nextCover);
        }, SWAP_MS * 2);
      }
    } else {
      setPlaying(isPlaying);
      setCover(nextCover);
      if (isPlaying) {
        if (!lastIsPlaying) {
          handlePlay(false);
        } else if (!drifting) {
          startDrift();
        }
      } else if (lastIsPlaying) {
        handlePause();
      }
    }

    lastIsPlaying = isPlaying;
  }
});
