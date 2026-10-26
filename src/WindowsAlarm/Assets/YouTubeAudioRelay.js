(() => {
  'use strict';
  if (location.protocol !== 'https:' || location.hostname !== 'www.youtube.com' || !location.pathname.startsWith('/embed/')) return;
  if (window.__windowsAlarmRelay) return;
  const session = '__ALARM_SESSION__';
  const send = fields => window.chrome.webview.postMessage({ session, ...fields });
  let context, source, processor, currentVideo, stopped = false;
  function stop() {
    stopped = true; clearInterval(poll);
    try { source?.disconnect(); processor?.disconnect(); context?.close(); } catch (_) {}
  }
  window.__windowsAlarmRelay = { stop };
  window.addEventListener('pagehide', stop, { once: true });
  async function attach() {
    if (stopped) return;
    const video = document.querySelector('video');
    if (!video || video === currentVideo) return;
    try {
      if (!context) {
        context = new AudioContext({ sampleRate: 48000 });
        if (context.sampleRate !== 48000) throw new Error('Unexpected sample rate');
        processor = context.createScriptProcessor(2048, 2, 2);
        processor.onaudioprocess = event => {
          if (stopped) return;
          const left = event.inputBuffer.getChannelData(0);
          const right = event.inputBuffer.numberOfChannels > 1 ? event.inputBuffer.getChannelData(1) : left;
          const bytes = new Uint8Array(left.length * 4), view = new DataView(bytes.buffer);
          for (let i = 0; i < left.length; i++) {
            view.setInt16(i * 4, Math.round(Math.max(-1, Math.min(1, left[i])) * 32767), true);
            view.setInt16(i * 4 + 2, Math.round(Math.max(-1, Math.min(1, right[i])) * 32767), true);
          }
          // The browser's output stays silent; the native Alerts stream is the only audible output.
          for (let channel = 0; channel < event.outputBuffer.numberOfChannels; channel++) event.outputBuffer.getChannelData(channel).fill(0);
          let binary = '';
          for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
          send({ type: 'pcm', data: btoa(binary) });
        };
        processor.connect(context.destination);
      }
      source?.disconnect();
      source = context.createMediaElementSource(video);
      source.connect(processor); currentVideo = video;
      await context.resume();
      send({ type: 'relay-ready', state: context.state });
    } catch (error) { send({ type: 'relay-error', message: String(error).slice(0, 160) }); stop(); }
  }
  const poll = setInterval(attach, 250);
  attach();
})();
