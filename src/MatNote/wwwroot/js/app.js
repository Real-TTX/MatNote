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

  // "Copy to clipboard" in the export dialog: the same request as the download (the chosen format, linked notes or not), but the text goes to the clipboard. Only text formats can: a Word
  // file or a PDF is a download. A web page is copied formatted (it pastes into a mail or a document as it looks) with its source as the plain text.
  var TEXT_FORMATS = ['Txt', 'Markdown', 'Html', 'Rtf', 'Csv', 'Json'];

  function chosenFormat(form) {
    var radio = form.querySelector('input[name="format"]:checked');
    return radio ? radio.value : '';
  }

  function syncExportCopy(form) {
    var button = form.querySelector('[data-export-copy]');
    if (button) { button.hidden = TEXT_FORMATS.indexOf(chosenFormat(form)) === -1; }
  }

  // the export dialog remembers what was chosen last (the format, the linked notes, the attachments): Markdown to begin with, which is what a prompt wants
  var EXPORT_KEY = 'mn.export';

  function restoreExport(form) {
    var saved = null;
    try { saved = JSON.parse(localStorage.getItem(EXPORT_KEY) || 'null'); } catch (e) { saved = null; }
    if (!saved) { return; }
    var radio = saved.format && form.querySelector('input[name="format"][value="' + saved.format + '"]');
    if (radio) { radio.checked = true; }
    ['linked', 'attachments'].forEach(function (name) {
      var box = form.querySelector('input[type=checkbox][name="' + name + '"]');
      if (box && typeof saved[name] === 'boolean') { box.checked = saved[name]; }
    });
  }

  function rememberExport(form) {
    var saved = {};
    try { saved = JSON.parse(localStorage.getItem(EXPORT_KEY) || '{}') || {}; } catch (e) { saved = {}; }
    var radio = form.querySelector('input[name="format"]:checked');
    if (radio) { saved.format = radio.value; }
    ['linked', 'attachments'].forEach(function (name) {
      var box = form.querySelector('input[type=checkbox][name="' + name + '"]');
      if (box) { saved[name] = box.checked; }
    });
    try { localStorage.setItem(EXPORT_KEY, JSON.stringify(saved)); } catch (e) { /* ignore */ }
  }

  Array.prototype.forEach.call(document.querySelectorAll('form [data-export-copy]'), function (button) {
    var form = button.closest('form');
    restoreExport(form);
    syncExportCopy(form);
    form.addEventListener('change', function () { syncExportCopy(form); rememberExport(form); });
  });

  document.addEventListener('click', function (e) {
    var button = e.target.closest && e.target.closest('[data-export-copy]');
    if (!button) { return; }
    var form = button.closest('form');
    var format = chosenFormat(form);
    var query = new URLSearchParams();
    query.set('format', format);
    Array.prototype.forEach.call(form.querySelectorAll('input[type=hidden], input[type=checkbox]:checked'), function (field) { if (field.name !== 'attachments') { query.set(field.name, field.value); } });
    fetch(form.getAttribute('action') + '?' + query.toString(), { credentials: 'same-origin' }).then(function (r) {
      if (!r.ok) { throw new Error('http ' + r.status); }
      return r.text();
    }).then(function (text) {
      // a note with places to fill in ({{name}}) asks for the values first (params.js); cancel = nothing is copied
      return window.mnParams ? window.mnParams.fill(text) : text;
    }).then(function (text) {
      if (text === null) { return false; }
      if (format === 'Html' && window.ClipboardItem && navigator.clipboard && navigator.clipboard.write && window.isSecureContext) {
        // the content of the page only (its style block would be run by the browser when it prepares the clipboard); the whole page as the plain text
        var body = new DOMParser().parseFromString(text, 'text/html').body.innerHTML;
        return navigator.clipboard.write([new ClipboardItem({ 'text/html': new Blob([body], { type: 'text/html' }), 'text/plain': new Blob([text], { type: 'text/plain' }) })]);
      }
      return copyText(text);
    }).then(function (cancelled) {
      if (cancelled === false) { return; }
      toast('Copied to the clipboard.');
      var dialog = button.closest('dialog');
      if (dialog && dialog.close) { dialog.close(); }
    }, function () { toast('The text could not be copied.', 'danger'); });
  });

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
      var on = id !== null && String(id) === row.getAttribute('data-note-id');
      row.classList.toggle('is-selected', on);
      var link = row.querySelector('[data-open-pane]');
      if (link) { if (on) { link.setAttribute('aria-current', 'true'); } else { link.removeAttribute('aria-current'); } }
    });
    var url = new URL(location.href);
    if (id) { url.searchParams.set('id', id); } else { url.searchParams.delete('id'); }
    history.replaceState(null, '', url.toString());
  }

  // What is not the list of notes (the pager, the empty message): its markup without the list
  function restOf(box) {
    var copy = box.cloneNode(true);
    var list = copy.querySelector('.mn-notelist');
    if (list) { list.remove(); }
    return copy.innerHTML;
  }

  // The list is brought up to date row by row and nothing that is the same is touched, so that it never flickers while a note is written. A changed row changes where it
  // stands (the note that was just saved does not jump to the top under the pointer); a new note is put where the server has it, a removed one goes.
  function patch(mine, fresh) {
    var have = mine.querySelector('.mn-notelist');
    var want = fresh.querySelector('.mn-notelist');
    if (!have || !want || restOf(mine) !== restOf(fresh)) {
      if (mine.innerHTML !== fresh.innerHTML) {
        mine.innerHTML = fresh.innerHTML;
        if (window.MatUi && window.MatUi.refresh) { window.MatUi.refresh(mine); }
      }
      return;
    }

    var rows = {};
    Array.prototype.forEach.call(have.children, function (li) { rows[li.getAttribute('data-note-id')] = li; });
    var ids = Array.prototype.map.call(want.children, function (li) { return li.getAttribute('data-note-id'); });
    Object.keys(rows).forEach(function (id) { if (ids.indexOf(id) === -1) { rows[id].remove(); } });
    Array.prototype.forEach.call(want.children, function (li, i) {
      var cur = rows[ids[i]];
      if (!cur) {
        have.insertBefore(document.importNode(li, true), have.children[i] || null);
        return;
      }
      if (cur.innerHTML !== li.innerHTML) { cur.innerHTML = li.innerHTML; }
      if (cur.className !== li.className) { cur.className = li.className; }
      Array.prototype.forEach.call(li.attributes, function (a) { if (cur.getAttribute(a.name) !== a.value) { cur.setAttribute(a.name, a.value); } });
      Array.prototype.slice.call(cur.attributes).forEach(function (a) { if (!li.hasAttribute(a.name)) { cur.removeAttribute(a.name); } });
    });
  }

  function refresh() {
    fetch(location.href, { credentials: 'same-origin' }).then(function (r) { return r.text(); }).then(function (html) {
      var next = new DOMParser().parseFromString(html, 'text/html');
      var fresh = next.querySelector('[data-split-rows]');
      var mine = document.querySelector('[data-split-rows]');
      if (!fresh || !mine) { return; }
      patch(mine, fresh);
      var info = next.querySelector('.mat-toolbar__info');
      var shown = document.querySelector('.mat-toolbar__info');
      if (info && shown && shown.textContent !== info.textContent) { shown.textContent = info.textContent; }
    }).catch(function () { /* the list stays as it is */ });
  }

  document.addEventListener('mn:refresh-list', refresh);

  document.addEventListener('click', function (e) {
    var link = e.target.closest && e.target.closest('[data-open-pane]');
    if (!link || !wide.matches || e.defaultPrevented || e.button || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) { return; }
    e.preventDefault();
    var row = link.closest('[data-note-id]');
    if (row) { select(row.getAttribute('data-note-id')); }
    // a note of the person opens in the mode they work in
    var href = link.href;
    if (row && row.getAttribute('data-edit') === 'true' && document.cookie.indexOf('mn.mode=edit') !== -1) { href = '/Notes/Edit?id=' + row.getAttribute('data-note-id'); }
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
