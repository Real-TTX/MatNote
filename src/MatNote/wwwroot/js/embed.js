/* MatNotes: what a page does that is shown inside the note pane of the list (_LayoutEmbed, "split" layout).

   - Links that stay with the note (read, edit, files, export) stay in the pane and keep ?embed=1; links that leave it (a folder, a tag, sharing) open in the whole window;
     links to other sites open in a new tab.
   - After every load the page tells the list which note it shows (body[data-note-id]), so that the list marks the row and fetches its rows again.
     window.mnEmbedTell(extra) does the same on demand (autosave.js after a save). */
(function () {
  'use strict';

  var STAY = /^\/Notes\/(View|Edit|Pane|File|Export)$/;
  var WITH_EMBED = /^\/Notes\/(View|Edit|Pane)$/;

  Array.prototype.forEach.call(document.querySelectorAll('a[href]'), function (a) {
    if (a.target || a.getAttribute('href').charAt(0) === '#') { return; }
    var url;
    try { url = new URL(a.href, location.href); } catch (e) { return; }
    if (url.protocol !== 'http:' && url.protocol !== 'https:') { return; }
    if (url.origin !== location.origin) { a.target = '_blank'; a.rel = 'noopener noreferrer'; return; }
    if (!STAY.test(url.pathname)) { a.target = '_top'; return; }
    if (WITH_EMBED.test(url.pathname) && !url.searchParams.has('embed')) {
      url.searchParams.set('embed', '1');
      a.setAttribute('href', url.pathname + url.search);
    }
  });

  var parentWindow = window.parent !== window ? window.parent : null;

  function tell(extra) {
    if (!parentWindow) { return; }
    var id = document.body.getAttribute('data-note-id');
    var message = { mn: 'pane', id: id ? Number(id) : null };
    Object.keys(extra || {}).forEach(function (key) { message[key] = extra[key]; });
    parentWindow.postMessage(message, location.origin);
  }

  window.mnEmbedTell = tell;
  tell();
})();
