(function () {
  try {
    if (window.__PGB_SHOUT_HOOK__) { return; }
    var ENDPOINT = 'http://127.0.0.1:18123';
    var seen = {};
    var DBG = { version: 3, wrapCalls: 0, parsed: 0, matched: 0, errors: 0, lastError: '', posted: 0, installed: 0, images: 0, voices: 0 };
    function post(path, payload) {
      try {
        fetch(ENDPOINT + path, {
          method: 'POST',
          headers: { 'Content-Type': 'text/plain;charset=UTF-8' },
          body: JSON.stringify(payload),
          keepalive: true
        }).then(function () { DBG.posted++; }).catch(function (e) { DBG.errors++; DBG.lastError = 'post:' + e; });
      } catch (e) { DBG.errors++; DBG.lastError = 'post-sync:' + e; }
    }
    function currentClass() {
      try { return String(localStorage.getItem('pgb-shout-class-id') || ''); } catch (e) { return ''; }
    }
    function targetsFor(obj) {
      var arr = [];
      ['target_classrooms', 'target_classroom', 'class_ids', 'class_id', 'classrooms', 'classroom'].forEach(function (k) {
        var v = obj[k];
        if (Array.isArray(v)) { v.forEach(function (x) { var s = String(x || '').trim(); if (s) { arr.push(s); } }); }
        else { var s = String(v || '').trim(); if (s) { arr.push(s); } }
      });
      return Array.from(new Set(arr));
    }
    function pickImage(o) {
      var img = o.image || o;
      var url = String((img && (img.url || img.imageUrl || img.image_url || img.imgUrl || img.fileUrl)) || '').trim();
      var b64 = String((img && (img.base64 || img.data)) || '').trim();
      return { url: /^https?:\/\//i.test(url) ? url : '', base64: b64 };
    }
    function pickVoice(o) {
      var v = o.voice || {};
      var url = String(v.url || v.voiceUrl || v.fileUrl || '').trim();
      var b64 = String(v.base64 || v.data || '').trim();
      var mime = String(v.mime || 'audio/webm');
      return { url: /^https?:\/\//i.test(url) ? url : '', base64: b64, mime: mime };
    }
    function parsePayload(msg) {
      var raw = String(msg == null ? '' : msg);
      var out = { text: '', speech: true, image: null, voice: null };
      try {
        var o = JSON.parse(raw);
        if (o && o.type === 'remote_shout_v2') {
          out.text = String(o.text || o.message || o.content || '');
          out.speech = (o.speechEnabled !== false) && (o.speech_enabled !== false);
          out.image = pickImage(o);
          out.voice = pickVoice(o);
          return out;
        }
        if (o && typeof o.text === 'string') { out.text = o.text; }
      } catch (e) {}
      var parts = raw.split('\u0000');
      if (!out.text && parts.length > 1) { out.text = parts[0] || ''; out.speech = parts[2] === '1'; }
      if (!out.text) { out.text = parts[0] || ''; }
      return out;
    }
    function b64ToBlobUrl(b64, mime) {
      try {
        var bin = atob(b64);
        var len = bin.length;
        var bytes = new Uint8Array(len);
        for (var i = 0; i < len; i++) { bytes[i] = bin.charCodeAt(i); }
        return URL.createObjectURL(new Blob([bytes], { type: mime || 'audio/webm' }));
      } catch (e) { return ''; }
    }
    function playVoice(voice) {
      try {
        var src = voice && voice.url ? voice.url : '';
        if (!src && voice && voice.base64) { src = b64ToBlobUrl(voice.base64, voice.mime); }
        if (!src) { return false; }
        var a = new Audio(src);
        a.volume = 1;
        var p = a.play();
        if (p && p.catch) { p.catch(function (e) { DBG.errors++; DBG.lastError = 'audio:' + e; }); }
        DBG.voices++;
        return true;
      } catch (e) { DBG.errors++; DBG.lastError = 'voice:' + e; return false; }
    }
    function fetchImageBlob(imageUrl, withCredentials) {
      var opt = { cache: 'no-store' };
      if (withCredentials) { opt.credentials = 'include'; }
      return fetch(imageUrl, opt).then(function (r) {
        if (!r.ok) { throw new Error('http ' + r.status); }
        return r.blob();
      });
    }
    function postShout(payload, imageUrl) {
      if (!imageUrl) { post('/shout', payload); return; }
      try {
        fetchImageBlob(imageUrl, false).catch(function () { return fetchImageBlob(imageUrl, true); }).then(function (blob) {
          if (!blob || blob.size === 0 || blob.size > 1200 * 1024) { post('/shout', payload); return; }
          var fr = new FileReader();
          fr.onload = function () {
            var s = String(fr.result || '');
            var i = s.indexOf(',');
            if (i > 0) { payload.imageBase64 = s.slice(i + 1); payload.imageMime = blob.type || 'image/png'; DBG.images++; }
            post('/shout', payload);
          };
          fr.onerror = function () { post('/shout', payload); };
          fr.readAsDataURL(blob);
        }).catch(function () { post('/shout', payload); });
      } catch (e) { post('/shout', payload); }
    }
    function handle(data, socket) {
      var obj;
      try { obj = JSON.parse(data); } catch (e) { DBG.errors++; DBG.lastError = 'json:' + e; return false; }
      DBG.parsed++;
      if (!obj || obj.action !== 'announcement') { return false; }
      DBG.matched++;
      var targets = targetsFor(obj);
      var cur = currentClass();
      if (targets.length > 0 && cur && targets.indexOf(cur) < 0) { return false; }
      var id = String(obj.announcement_id || '');
      if (id && seen[id]) { return true; }
      if (id) { seen[id] = Date.now(); }
      var payload = parsePayload(obj.message);
      var text = String(payload.text || '');
      var hasMedia = !!(payload.image && (payload.image.url || payload.image.base64)) || !!(payload.voice && (payload.voice.url || payload.voice.base64));
      if (!text) { text = hasMedia ? '（批改邦喊话：图片/语音）' : ''; }
      var voicePlayed = playVoice(payload.voice);
      try {
        if (socket && socket.readyState === 1 && id) {
          socket.send(JSON.stringify({ action: 'announcement_ack', announcement_id: id, mobile_client_id: String(obj.mobile_client_id || '') }));
        }
      } catch (e) {}
      postShout({
        source: 'pigaibang-hook',
        announcement_id: id,
        sender: String(obj.sender_name || '老师'),
        text: String(text).slice(0, 2000),
        speech: payload.speech !== false,
        image: (payload.image && payload.image.url) || '',
        voiceUrl: (payload.voice && payload.voice.url) || '',
        hasVoice: voicePlayed,
        sent_at: String(obj.sent_at || ''),
        url: location.href
      }, (payload.image && payload.image.url) || '');
      return true;
    }
    function makeWrapper(ws, fn) {
      return function (ev) {
        DBG.wrapCalls++;
        try {
          var d = typeof ev.data === 'string' ? ev.data : '';
          if (d && handle(d, ws)) { return; }
        } catch (e) { DBG.errors++; DBG.lastError = 'wrap:' + e; }
        return fn.call(ws, ev);
      };
    }
    function install(ws) {
      DBG.installed++;
      try {
        var desc = Object.getOwnPropertyDescriptor(WebSocket.prototype, 'onmessage');
        if (desc && desc.set) {
          Object.defineProperty(ws, 'onmessage', {
            configurable: true,
            enumerable: true,
            get: function () { return desc.get.call(ws); },
            set: function (fn) {
              if (typeof fn !== 'function') { desc.set.call(ws, fn); return; }
              desc.set.call(ws, makeWrapper(ws, fn));
            }
          });
        }
      } catch (e) { DBG.errors++; DBG.lastError = 'install:' + e; }
      try {
        var add = ws.addEventListener.bind(ws);
        ws.addEventListener = function (type, listener, opts) {
          if (type === 'message' && typeof listener === 'function') { return add(type, makeWrapper(ws, listener), opts); }
          return add(type, listener, opts);
        };
      } catch (e) {}
    }
    var Native = window.WebSocket;
    window.WebSocket = new Proxy(Native, {
      construct: function (target, args) {
        var ws = new target(args[0], args[1]);
        try { install(ws); } catch (e) {}
        return ws;
      }
    });
    window.__PGB_SHOUT_HOOK__ = { version: 3, endpoint: ENDPOINT, dbg: DBG, inject: function (obj) { return handle(JSON.stringify(obj), null); } };
    post('/hello', { source: 'pigaibang-hook', url: location.href, ua: navigator.userAgent });
    try {
      setTimeout(function () { post('/hello', { source: 'pigaibang-hook', beat: true, url: location.href }); }, 15000);
      setInterval(function () { post('/hello', { source: 'pigaibang-hook', beat: true, url: location.href }); }, 300000);
    } catch (e) {}
    try { console.log('[PGB-HOOK] installed @ ' + location.href); } catch (e) {}
  } catch (e) {
    try { console.error('[PGB-HOOK] install failed', e); } catch (x) {}
  }
})();
