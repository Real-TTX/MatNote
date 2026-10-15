/* MatNotes: the few behaviours that only this app has (the toolkit does the rest).

   data-copy-inner="#id"   copy the text of an element as the person reads it (line breaks of paragraphs and list items kept): the "Copy" button of a rich text note.
                           (data-copy="#id" of the toolkit copies the value of a field, which is what plain text and markdown notes use.)
   data-layout="split|list" the switch of the notes list between "list and note side by side" and "list alone" (the server remembers it in a cookie).
   data-open-pane         a link of the notes list: on a wide window the note opens in the pane at the right, otherwise the link works as a link.
   data-auto-reload="ms"  reload the page after that time: the page of a running backup, until the backup is done (the notice is no longer on the page then). */
(function () {
  'use strict';

  function toast(message, tone) {
    if (window.MatUi && window.MatUi.toast) { window.MatUi.toast(message, { tone: tone || 'success' }); }
  }

  function copyText(text) {
    if (navigator.clipboard && window.isSecureContext) { return navigator.clipboard.writeText(text); }
    return new Promise(function (resolve, reject) {
      var area = document.createElement('textarea');
      area.value = text;
      area.setAttribute('readonly', '');
      area.className = 'mat-sr-only';
      document.body.appendChild(area);
      area.select();
      try { document.execCommand('copy') ? resolve() : reject(new Error('copy failed')); } catch (e) { reject(e); } finally { area.remove(); }
    });
  }

  var auto = document.querySelector('[data-auto-reload]');
  if (auto) { setTimeout(function () { location.reload(); }, parseInt(auto.getAttribute('data-auto-reload'), 10) || 3000); }

  document.addEventListener('click', function (e) {
    var button = e.target.closest && e.target.closest('[data-copy-inner]');
    if (!button) { return; }
    var source = document.querySelector(button.getAttribute('data-copy-inner'));
    if (!source) { return; }
    // innerText follows the layout: a paragraph or a list item ends with a line break
    var text = (source.innerText || source.textContent || '').trim();
    copyText(text).then(function () { toast('Copied to the clipboard.'); }, function () { toast('The text could not be copied.', 'danger'); });
  });
})();

/* The notes list with the note beside it (Notes/Index, "split" layout): a click on a note opens it in the frame at the right; the frame tells this page which note it
   shows (embed.js) and this page marks the row, keeps the address (?id=) and fetches the rows again (a new title, a new note, a note in the trash). */
(function () {
  'use strict';

  var wide = window.matchMedia('(min-width: 1100px)');

  document.addEventListener('click', function (e) {
    var choice = e.target.closest && e.target.closest('[data-layout]');
    if (!choice) { return; }
    var url = new URL(location.href);
    url.searchParams.set('layout', choice.getAttribute('data-layout'));
    url.searchParams.delete('id');
    location.href = url.toString();
  });

  var split = document.querySelector('[data-split]');
  if (!split) { return; }
  var frame = split.querySelector('iframe');

  function paneUrl(href) {
    var url = new URL(href, location.href);
    url.searchParams.set('embed', '1');
    return url.pathname + url.search;
  }

  function select(id) {
    Array.prototype.forEach.call(document.querySelectorAll('[data-split-rows] [data-note-id]'), function (row) {
      row.classList.toggle('is-selected', id !== null && String(id) === row.getAttribute('data-note-id'));
    });
    var url = new URL(location.href);
    if (id) { url.searchParams.set('id', id); } else { url.searchParams.delete('id'); }
    history.replaceState(null, '', url.toString());
  }

  function refresh() {
    fetch(location.href, { credentials: 'same-origin' }).then(function (r) { return r.text(); }).then(function (html) {
      var next = new DOMParser().parseFromString(html, 'text/html');
      var fresh = next.querySelector('[data-split-rows]');
      var mine = document.querySelector('[data-split-rows]');
      if (!fresh || !mine) { return; }
      if (mine.innerHTML !== fresh.innerHTML) {
        mine.innerHTML = fresh.innerHTML;
        if (window.MatUi && window.MatUi.refresh) { window.MatUi.refresh(mine); }
      }
      var info = next.querySelector('.mat-toolbar__info');
      var shown = document.querySelector('.mat-toolbar__info');
      if (info && shown) { shown.textContent = info.textContent; }
    }).catch(function () { /* the list stays as it is */ });
  }

  document.addEventListener('click', function (e) {
    var link = e.target.closest && e.target.closest('[data-open-pane]');
    if (!link || !wide.matches || e.defaultPrevented || e.button || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) { return; }
    e.preventDefault();
    var row = link.closest('[data-note-id]');
    if (row) { select(row.getAttribute('data-note-id')); }
    // a note of the person opens in the mode they work in
    var href = link.href;
    if (row && row.hasAttribute('data-edit') && document.cookie.indexOf('mn.mode=edit') !== -1) { href = '/Notes/Edit?id=' + row.getAttribute('data-note-id'); }
    frame.contentWindow.location.replace(paneUrl(href));
  });

  window.addEventListener('message', function (e) {
    var data = e.data;
    if (e.origin !== location.origin || e.source !== frame.contentWindow || !data || data.mn !== 'pane') { return; }
    // the mode (reading or editing) of the note in the pane is the mode the next note opens in (also after a reload: the server reads the cookie)
    if (data.mode) { document.cookie = 'mn.mode=' + data.mode + '; path=/; max-age=31536000; samesite=lax'; }
    select(data.id || null);
    refresh();
  });
})();
