/* ==========================================================================
   MatNotes editor: a small rich text editor for the notes (no library, no CDN) and the "insert a link to a note" dialog.

   Markup (Pages/Notes/Edit.cshtml):
     <div class="mn-editor" data-editor>
       <textarea class="mat-textarea mn-editor__source" name="Content">...html...</textarea>
     </div>
   Without this script the textarea stays: the note can be edited as HTML. With it the textarea is hidden, a toolbar and an editable area are drawn, and the
   HTML of the area goes into the textarea on every change (the server sanitizes it again on save).

   Note links: a button with data-note-link-open="dialogId" opens the dialog (<dialog id data-note-link-dialog data-lookup-url="/Notes/Lookup">), whose search
   asks the server for notes the person may see. The choice is inserted at the cursor of the field that has the focus (data-note-link-target="#id") as
   - an anchor <a class="mn-note-link" data-note-id="42" href="/Notes/View?id=42">Title</a> in the rich text editor,
   - [Title](note:42) in a textarea (plain text and markdown).
   ========================================================================== */
(function () {
  'use strict';

  var doc = document;
  function $(sel, ctx) { return (ctx || doc).querySelector(sel); }
  function $$(sel, ctx) { return Array.prototype.slice.call((ctx || doc).querySelectorAll(sel)); }
  function icon(name) { return '<svg class="mat-icon" aria-hidden="true" focusable="false"><use href="#mat-i-' + name + '"></use></svg>'; }
  function esc(s) { return String(s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }

  // ---- the rich text editor ---------------------------------------------------------------------------------------------------------

  var BUTTONS = [
    { cmd: 'bold', icon: 'bold', label: 'Bold', key: 'b' },
    { cmd: 'italic', icon: 'italic', label: 'Italic', key: 'i' },
    { cmd: 'underline', icon: 'underline', label: 'Underline', key: 'u' },
    { cmd: 'strikeThrough', icon: 'strikethrough', label: 'Strikethrough' },
    '|',
    { cmd: 'formatBlock', arg: 'h2', icon: 'heading', label: 'Heading', block: 'h2' },
    { cmd: 'formatBlock', arg: 'blockquote', icon: 'quote', label: 'Quote', block: 'blockquote' },
    { cmd: 'formatBlock', arg: 'pre', icon: 'code', label: 'Code block', block: 'pre' },
    '|',
    { cmd: 'insertUnorderedList', icon: 'list', label: 'Bulleted list' },
    { cmd: 'insertOrderedList', icon: 'list-ordered', label: 'Numbered list' },
    { cmd: 'insertHorizontalRule', icon: 'minus', label: 'Divider' },
    '|',
    { action: 'link', icon: 'link', label: 'Link to a web page' },
    { action: 'note-link', icon: 'note-link', label: 'Link to a note' },
    '|',
    { cmd: 'undo', icon: 'undo', label: 'Undo' },
    { cmd: 'redo', icon: 'redo', label: 'Redo' },
    { cmd: 'removeFormat', icon: 'x', label: 'Clear formatting' },
    { action: 'source', icon: 'code', label: 'Edit the HTML' }
  ];

  function setupEditor(box) {
    var source = $('textarea', box);
    if (!source || box.getAttribute('data-ready')) { return; }
    box.setAttribute('data-ready', '1');

    var bar = doc.createElement('div');
    bar.className = 'mn-editor__bar';
    bar.setAttribute('role', 'toolbar');
    bar.setAttribute('aria-label', 'Formatting');

    var area = doc.createElement('div');
    area.className = 'mn-editor__area mn-doc';
    area.setAttribute('contenteditable', 'true');
    area.setAttribute('role', 'textbox');
    area.setAttribute('aria-multiline', 'true');
    area.setAttribute('aria-label', source.getAttribute('aria-label') || 'Text of the note');
    area.setAttribute('data-placeholder', 'Start writing ...');
    area.id = source.id + '-editor';
    area.innerHTML = source.value;
    box.setAttribute('data-editor-ready', '');

    BUTTONS.forEach(function (b) {
      if (b === '|') {
        var sep = doc.createElement('span'); sep.className = 'mn-editor__sep'; sep.setAttribute('aria-hidden', 'true'); bar.appendChild(sep); return;
      }
      var btn = doc.createElement('button');
      btn.type = 'button';
      btn.className = 'mat-btn mat-btn--ghost mat-btn--icon mat-btn--sm';
      btn.setAttribute('aria-label', b.label);
      btn.setAttribute('data-tip', b.label);
      btn.innerHTML = icon(b.icon);
      if (b.cmd) { btn.setAttribute('data-cmd', b.cmd); }
      if (b.arg) { btn.setAttribute('data-arg', b.arg); }
      if (b.action) { btn.setAttribute('data-action', b.action); }
      // a click on the button must not take the selection out of the text
      btn.addEventListener('mousedown', function (e) { e.preventDefault(); });
      btn.addEventListener('click', function () { run(b); });
      bar.appendChild(btn);
    });

    source.hidden = true;
    box.insertBefore(bar, source);
    box.insertBefore(area, source);

    // paragraphs, and elements instead of style attributes (the page forbids inline style)
    try { doc.execCommand('defaultParagraphSeparator', false, 'p'); doc.execCommand('styleWithCSS', false, false); } catch (e) { /* older browsers */ }

    function sync() { source.value = area.innerHTML; }
    function run(b) {
      if (b.action === 'source') { toggleSource(); return; }
      if (b.action === 'link') { askLink(); return; }
      if (b.action === 'note-link') { openNoteDialog(area); return; }
      area.focus();
      try { doc.execCommand('styleWithCSS', false, false); } catch (e) { /* ignore */ }
      if (b.cmd === 'formatBlock') {
        var current = doc.queryCommandValue('formatBlock').toLowerCase().replace(/[<>]/g, '');
        doc.execCommand('formatBlock', false, current === b.arg ? 'p' : b.arg);
      } else {
        doc.execCommand(b.cmd, false, null);
      }
      sync();
      mark();
    }

    function askLink() {
      var saved = saveRange();
      var ask = window.MatUi && window.MatUi.prompt
        ? window.MatUi.prompt('Address of the web page', { title: 'Link', placeholder: 'https://', ok: 'Add the link' })
        : Promise.resolve(window.prompt('Address of the web page', 'https://'));
      ask.then(function (url) {
        if (!url) { return; }
        url = url.trim();
        if (!/^(https?:\/\/|mailto:)/i.test(url)) { url = 'https://' + url; }
        area.focus();
        restoreRange(saved);
        if (doc.getSelection().isCollapsed) { doc.execCommand('insertHTML', false, '<a href="' + esc(url) + '">' + esc(url) + '</a>'); }
        else { doc.execCommand('createLink', false, url); }
        sync();
      });
    }

    function saveRange() { var s = doc.getSelection(); return s.rangeCount && area.contains(s.anchorNode) ? s.getRangeAt(0).cloneRange() : null; }
    function restoreRange(r) { if (r) { var s = doc.getSelection(); s.removeAllRanges(); s.addRange(r); } }

    function toggleSource() {
      var showing = !source.hidden;
      if (showing) { area.innerHTML = source.value; source.hidden = true; area.hidden = false; bar.classList.remove('is-source'); }
      else { sync(); source.hidden = false; area.hidden = true; bar.classList.add('is-source'); }
      $$('[data-cmd], [data-action="link"], [data-action="note-link"]', bar).forEach(function (btn) { btn.disabled = !showing; });
      var srcBtn = $('[data-action="source"]', bar);
      if (srcBtn) { srcBtn.classList.toggle('is-on', !showing); }
    }

    function mark() {
      $$('[data-cmd]', bar).forEach(function (btn) {
        var cmd = btn.getAttribute('data-cmd'); var on = false;
        try {
          if (cmd === 'formatBlock') { on = doc.queryCommandValue('formatBlock').toLowerCase().replace(/[<>]/g, '') === btn.getAttribute('data-arg'); }
          else if (cmd !== 'undo' && cmd !== 'redo' && cmd !== 'removeFormat' && cmd !== 'insertHorizontalRule') { on = doc.queryCommandState(cmd); }
        } catch (e) { on = false; }
        btn.classList.toggle('is-on', on);
        btn.setAttribute('aria-pressed', on ? 'true' : 'false');
      });
    }

    area.addEventListener('input', sync);
    area.addEventListener('keyup', mark);
    area.addEventListener('mouseup', mark);
    doc.addEventListener('selectionchange', function () { if (doc.activeElement === area) { mark(); } });
    area.addEventListener('keydown', function (e) {
      if ((e.ctrlKey || e.metaKey) && !e.shiftKey && !e.altKey) {
        var map = { b: 'bold', i: 'italic', u: 'underline' };
        if (map[e.key.toLowerCase()]) { e.preventDefault(); doc.execCommand(map[e.key.toLowerCase()], false, null); sync(); mark(); }
        if (e.key.toLowerCase() === 'k') { e.preventDefault(); askLink(); }
      }
    });
    // pasted text comes in as plain text: the formatting of another page or program would bring its own styles
    area.addEventListener('paste', function (e) {
      var data = e.clipboardData || window.clipboardData;
      if (!data) { return; }
      e.preventDefault();
      doc.execCommand('insertText', false, data.getData('text/plain'));
      sync();
    });
    var form = source.form;
    if (form) { form.addEventListener('submit', function () { if (source.hidden) { sync(); } else { area.innerHTML = source.value; } }); }
    box.noteLinkTarget = area;
  }

  // ---- links to notes -----------------------------------------------------------------------------------------------------------------

  var dialog = null;
  var target = null;       // the rich text area or the textarea that gets the link
  var savedRange = null;
  var timer = null;

  function openNoteDialog(field) {
    dialog = dialog || $('[data-note-link-dialog]');
    if (!dialog) { return; }
    target = field;
    var s = doc.getSelection();
    savedRange = field.isContentEditable && s.rangeCount && field.contains(s.anchorNode) ? s.getRangeAt(0).cloneRange() : null;
    var input = $('input[type=search]', dialog);
    input.value = '';
    $('.mn-lookup__list', dialog).innerHTML = '';
    if (typeof dialog.showModal === 'function') { dialog.showModal(); }
    search('');
    setTimeout(function () { input.focus(); }, 30);
  }

  function search(q) {
    var list = $('.mn-lookup__list', dialog);
    var url = dialog.getAttribute('data-lookup-url') + '?q=' + encodeURIComponent(q) + (dialog.getAttribute('data-exclude') ? '&exclude=' + encodeURIComponent(dialog.getAttribute('data-exclude')) : '');
    fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
      .then(function (r) { return r.ok ? r.json() : []; })
      .then(function (items) {
        if (!items.length) { list.innerHTML = '<li class="mat-muted mn-lookup__none">No note found.</li>'; return; }
        list.innerHTML = items.map(function (n) {
          return '<li><button type="button" class="mat-menu__item" data-id="' + n.id + '" data-title="' + esc(n.title) + '">' + icon(n.icon || 'file-text') + '<span>' + esc(n.title) + (n.folder ? ' <small class="mat-muted">' + esc(n.folder) + '</small>' : '') + '</span></button></li>';
        }).join('');
      })
      .catch(function () { list.innerHTML = '<li class="mat-muted mn-lookup__none">The notes could not be loaded.</li>'; });
  }

  function insertNoteLink(id, title) {
    if (!target) { return; }
    // a modal dialog makes the rest of the page inert: close it first, then put the link into the field
    if (dialog && dialog.open) { dialog.close(); }
    if (target.isContentEditable) {
      target.focus();
      if (savedRange) { var s = doc.getSelection(); s.removeAllRanges(); s.addRange(savedRange); }
      var html = '<a class="mn-note-link" data-note-id="' + id + '" href="/Notes/View?id=' + id + '">' + esc(title) + '</a>&nbsp;';
      doc.execCommand('insertHTML', false, html);
      target.dispatchEvent(new Event('input', { bubbles: true }));
    } else {
      var token = '[' + title.replace(/[\[\]]/g, '') + '](note:' + id + ')';
      var start = target.selectionStart || 0, end = target.selectionEnd || 0;
      target.value = target.value.slice(0, start) + token + target.value.slice(end);
      target.focus();
      target.selectionStart = target.selectionEnd = start + token.length;
      target.dispatchEvent(new Event('input', { bubbles: true }));
    }
  }

  doc.addEventListener('click', function (e) {
    var open = e.target.closest && e.target.closest('[data-note-link-open]');
    if (open) {
      var field = $(open.getAttribute('data-note-link-open'));
      if (field) { e.preventDefault(); openNoteDialog(field); }
      return;
    }
    var pick = e.target.closest && e.target.closest('.mn-lookup__list [data-id]');
    if (pick) { insertNoteLink(pick.getAttribute('data-id'), pick.getAttribute('data-title')); }
  });

  doc.addEventListener('input', function (e) {
    if (e.target.matches && e.target.matches('[data-note-link-dialog] input[type=search]')) {
      clearTimeout(timer);
      var q = e.target.value;
      timer = setTimeout(function () { search(q); }, 200);
    }
  });

  doc.addEventListener('keydown', function (e) {
    // Enter in the search field must not submit the note
    if (e.key === 'Enter' && e.target.matches && e.target.matches('[data-note-link-dialog] input[type=search]')) {
      e.preventDefault();
      var first = $('.mn-lookup__list [data-id]', dialog);
      if (first) { first.click(); }
    }
  });

  $$('[data-editor]').forEach(setupEditor);
})();
