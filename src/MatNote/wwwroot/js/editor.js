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

    // the textarea is what is posted; an input event tells the form that it changed (autosave.js)
    function sync() { source.value = area.innerHTML; source.dispatchEvent(new Event('input', { bubbles: true })); }
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

    // Deleting at the edge of a block: the browser joins the next block into the first one and the first one's kind wins, so the line below a deleted heading becomes a heading.
    // What stays is the kind of the block whose text stays: it is noted before the deletion and set again after it. Backspace at the start of a heading, quote or code block makes
    // it a paragraph first (as in a word processor) instead of pulling it into the block above.
    var BLOCKS = 'h1,h2,h3,h4,h5,h6,p,blockquote,pre,li,div';
    var KEEP = null;

    function blockOf(node) {
      var el = node && (node.nodeType === 1 ? node : node.parentElement);
      var block = el && el.closest(BLOCKS);
      return block && block !== area && area.contains(block) ? block : null;
    }

    function startsBlock(range, block) {
      var before = doc.createRange();
      before.setStart(block, 0);
      before.setEnd(range.startContainer, range.startOffset);
      return before.toString() === '' && !before.cloneContents().querySelector('img,hr,table');
    }

    function emptyBlock(block) { return block.textContent.replace(/​/g, '').trim() === '' && !block.querySelector('img,hr,table'); }

    area.addEventListener('beforeinput', function (e) {
      KEEP = null;
      var kind = e.inputType;
      if (kind !== 'deleteContentBackward' && kind !== 'deleteContentForward' && kind !== 'deleteByCut') { return; }
      var selection = doc.getSelection();
      if (!selection.rangeCount) { return; }
      var range = selection.getRangeAt(0);
      var first = blockOf(range.startContainer);
      var last = blockOf(range.endContainer);
      if (!first || !last) { return; }
      var plain = function (b) { return b.tagName !== 'LI' && b.tagName !== 'DIV'; };
      if (!range.collapsed) {
        // from the start of a block into another one: what stays is the text of the last block, with its kind
        if (first !== last && !first.contains(last) && !last.contains(first) && plain(first) && plain(last) && first.tagName !== last.tagName && startsBlock(range, first)) { KEEP = last.tagName.toLowerCase(); }
      } else if (kind === 'deleteContentForward') {
        var next = first.nextElementSibling;
        if (next && emptyBlock(first) && plain(first) && plain(next) && first.tagName !== next.tagName) { KEEP = next.tagName.toLowerCase(); }
      } else if (kind === 'deleteContentBackward' && /^(H[1-6]|BLOCKQUOTE|PRE)$/.test(first.tagName) && startsBlock(range, first)) {
        e.preventDefault();
        doc.execCommand('formatBlock', false, 'p');
        sync();
        mark();
      }
    });

    area.addEventListener('input', function () {
      if (KEEP) {
        var kind = KEEP;
        KEEP = null;
        var selection = doc.getSelection();
        var block = selection.rangeCount ? blockOf(selection.getRangeAt(0).startContainer) : null;
        if (block && block.tagName.toLowerCase() !== kind) { doc.execCommand('formatBlock', false, kind); }
      }
      sync();
    });
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
    attachPicker(area);
    attachLinkTip(area);
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

  // ---- OneNote style links: type [[ and a few letters, Ctrl + click or the little bar on a link opens it ----------------------------------

  var embedded = doc.body.getAttribute('data-embed') === 'true';

  function noteUrl(id) { return '/Notes/View?id=' + id + (embedded ? '&embed=1' : ''); }

  // opens a note in this window after what is typed has been saved (autosave.js)
  function openNote(id) {
    var go = function () { window.location.href = noteUrl(id); };
    (window.mnSaveNow ? window.mnSaveNow() : Promise.resolve()).then(go, go);
  }

  function textareaCaret(field) {
    var cs = window.getComputedStyle(field);
    var mirror = doc.createElement('div');
    ['boxSizing', 'width', 'fontFamily', 'fontSize', 'fontWeight', 'fontStyle', 'letterSpacing', 'lineHeight', 'tabSize',
      'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft', 'borderTopWidth', 'borderRightWidth', 'borderBottomWidth', 'borderLeftWidth'].forEach(function (p) { mirror.style[p] = cs[p]; });
    mirror.style.position = 'fixed'; mirror.style.visibility = 'hidden'; mirror.style.top = '0'; mirror.style.left = '-9999px';
    mirror.style.whiteSpace = 'pre-wrap'; mirror.style.overflowWrap = 'break-word';
    mirror.textContent = field.value.slice(0, field.selectionStart);
    var mark = doc.createElement('span'); mark.textContent = '​'; mirror.appendChild(mark);
    doc.body.appendChild(mirror);
    var f = field.getBoundingClientRect(), m = mirror.getBoundingClientRect(), s = mark.getBoundingClientRect();
    var h = parseFloat(cs.lineHeight) || 20;
    var rect = { left: f.left + (s.left - m.left) - field.scrollLeft, top: f.top + (s.top - m.top) - field.scrollTop, height: h };
    rect.bottom = rect.top + h;
    mirror.remove();
    return rect;
  }

  function caretRect(field) {
    if (!field.isContentEditable) { return textareaCaret(field); }
    var s = doc.getSelection();
    if (s.rangeCount) {
      var r = s.getRangeAt(0).cloneRange();
      r.collapse(true);
      var rects = r.getClientRects();
      if (rects.length) { return rects[0]; }
      var b = r.getBoundingClientRect();
      if (b.width || b.height) { return b; }
    }
    return field.getBoundingClientRect();
  }

  function placeNear(pop, rect) {
    pop.hidden = false;
    var w = pop.offsetWidth, h = pop.offsetHeight;
    var left = Math.max(8, Math.min(rect.left, window.innerWidth - w - 8));
    var top = rect.bottom + 4;
    if (top + h > window.innerHeight - 8 && rect.top - h - 4 > 8) { top = rect.top - h - 4; }
    pop.style.left = left + 'px';
    pop.style.top = top + 'px';
  }

  // the list that follows the cursor after [[
  var picker = null;
  var pick = null;   // { field, query, range | start + end, items, index, loaded }
  var pickTimer = null;

  function ensurePicker() {
    if (picker) { return picker; }
    picker = doc.createElement('div');
    picker.className = 'mn-linkpop';
    picker.setAttribute('role', 'listbox');
    picker.setAttribute('aria-label', 'Notes to link');
    picker.hidden = true;
    // a click must not take the cursor out of the text
    picker.addEventListener('mousedown', function (e) { e.preventDefault(); });
    picker.addEventListener('click', function (e) {
      var item = e.target.closest('[data-id]');
      if (item && pick) { choose(Number(item.getAttribute('data-index'))); }
    });
    doc.body.appendChild(picker);
    return picker;
  }

  function closePicker() {
    clearTimeout(pickTimer);
    pick = null;
    if (picker) { picker.hidden = true; }
  }

  function renderPicker() {
    var pop = ensurePicker();
    if (!pick.items.length) {
      pop.innerHTML = '<p class="mat-muted mn-linkpop__none">' + (pick.loaded ? 'No note found.' : 'Searching …') + '</p>';
    } else {
      pop.innerHTML = pick.items.map(function (n, i) {
        return '<button type="button" role="option" class="mat-menu__item' + (i === pick.index ? ' is-active' : '') + '" data-id="' + n.id + '" data-index="' + i + '" aria-selected="' + (i === pick.index ? 'true' : 'false') + '">' +
          icon(n.icon || 'file-text') + '<span>' + esc(n.title) + (n.folder ? ' <small class="mat-muted">' + esc(n.folder) + '</small>' : '') + '</span></button>';
      }).join('');
    }
    placeNear(pop, caretRect(pick.field));
  }

  function lookup() {
    var state = pick;
    if (!state) { return; }
    clearTimeout(pickTimer);
    pickTimer = setTimeout(function () {
      var dlg = $('[data-note-link-dialog]');
      var exclude = dlg ? dlg.getAttribute('data-exclude') : '';
      fetch('/Notes/Lookup?q=' + encodeURIComponent(state.query) + (exclude ? '&exclude=' + encodeURIComponent(exclude) : ''), { credentials: 'same-origin', headers: { Accept: 'application/json' } })
        .then(function (r) { return r.ok ? r.json() : []; })
        .then(function (items) {
          if (pick !== state) { return; }
          state.items = items.slice(0, 8);
          state.loaded = true;
          state.index = 0;
          renderPicker();
        })
        .catch(function () { /* the list stays as it was */ });
    }, 120);
  }

  // where the text before the cursor ends with [[query
  function trigger(field) {
    if (field.isContentEditable) {
      var s = doc.getSelection();
      if (!s.rangeCount || !s.isCollapsed || !field.contains(s.anchorNode) || s.anchorNode.nodeType !== 3) { return null; }
      var before = s.anchorNode.nodeValue.slice(0, s.anchorOffset);
      var m = /\[\[([^\[\]\n]{0,60})$/.exec(before);
      if (!m) { return null; }
      var range = doc.createRange();
      range.setStart(s.anchorNode, s.anchorOffset - m[0].length);
      range.setEnd(s.anchorNode, s.anchorOffset);
      return { query: m[1], range: range };
    }
    if (field.selectionStart !== field.selectionEnd) { return null; }
    var text = field.value.slice(0, field.selectionStart);
    var t = /\[\[([^\[\]\n]{0,60})$/.exec(text);
    return t ? { query: t[1], start: field.selectionStart - t[0].length, end: field.selectionStart } : null;
  }

  function update(field) {
    var t = trigger(field);
    if (!t) { closePicker(); return; }
    var fresh = !pick || pick.field !== field;
    var changed = fresh || pick.query !== t.query;
    pick = { field: field, query: t.query, range: t.range, start: t.start, end: t.end, items: fresh ? [] : pick.items, index: fresh ? 0 : pick.index, loaded: fresh ? false : pick.loaded };
    renderPicker();
    if (changed) { lookup(); }
  }

  function choose(index) {
    var state = pick;
    var n = state && state.items[index];
    if (!n) { return; }
    var field = state.field;
    closePicker();
    if (field.isContentEditable) {
      field.focus();
      var s = doc.getSelection();
      s.removeAllRanges();
      s.addRange(state.range);
      doc.execCommand('insertHTML', false, '<a class="mn-note-link" data-note-id="' + n.id + '" href="/Notes/View?id=' + n.id + '">' + esc(n.title) + '</a>&nbsp;');
      field.dispatchEvent(new Event('input', { bubbles: true }));
    } else {
      var token = '[' + n.title.replace(/[\[\]]/g, '') + '](note:' + n.id + ') ';
      field.value = field.value.slice(0, state.start) + token + field.value.slice(state.end);
      field.focus();
      field.selectionStart = field.selectionEnd = state.start + token.length;
      field.dispatchEvent(new Event('input', { bubbles: true }));
    }
  }

  function attachPicker(field) {
    field.addEventListener('input', function () { update(field); });
    field.addEventListener('click', function () { if (pick) { update(field); } });
    field.addEventListener('blur', function () { setTimeout(function () { if (doc.activeElement !== field) { closePicker(); } }, 120); });
    field.addEventListener('keydown', function (e) {
      if (!pick || pick.field !== field) { return; }
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        if (!pick.items.length) { return; }
        e.preventDefault();
        pick.index = (pick.index + (e.key === 'ArrowDown' ? 1 : pick.items.length - 1)) % pick.items.length;
        renderPicker();
      } else if ((e.key === 'Enter' || e.key === 'Tab') && pick.items.length) {
        e.preventDefault();
        choose(pick.index);
      } else if (e.key === 'Escape') {
        e.preventDefault();
        closePicker();
      }
    });
  }

  // the little bar over a link to a note in the rich text: open it, or take the link away
  var tip = null;
  var tipLink = null;

  function ensureTip() {
    if (tip) { return tip; }
    tip = doc.createElement('div');
    tip.className = 'mn-linkpop mn-linkpop--tip';
    tip.hidden = true;
    tip.innerHTML = '<button type="button" class="mat-btn mat-btn--ghost mat-btn--sm" data-act="open">' + icon('external') + '<span>Open</span></button>' +
      '<button type="button" class="mat-btn mat-btn--ghost mat-btn--sm" data-act="unlink">' + icon('x') + '<span>Remove link</span></button>';
    tip.addEventListener('mousedown', function (e) { e.preventDefault(); });
    tip.addEventListener('click', function (e) {
      var b = e.target.closest('[data-act]');
      if (!b || !tipLink) { return; }
      var link = tipLink;
      if (b.getAttribute('data-act') === 'open') { openNote(link.getAttribute('data-note-id')); return; }
      var area = link.closest('[contenteditable]');
      link.replaceWith(doc.createTextNode(link.textContent));
      tip.hidden = true;
      tipLink = null;
      if (area) { area.dispatchEvent(new Event('input', { bubbles: true })); }
    });
    doc.body.appendChild(tip);
    return tip;
  }

  function attachLinkTip(area) {
    function linkAt(node) {
      var el = node && (node.nodeType === 1 ? node : node.parentNode);
      return el && el.closest ? el.closest('a.mn-note-link[data-note-id]') : null;
    }
    function check() {
      var s = doc.getSelection();
      var link = s.rangeCount ? linkAt(s.anchorNode) : null;
      if (!link || !area.contains(link)) { if (tip) { tip.hidden = true; } tipLink = null; return; }
      tipLink = link;
      placeNear(ensureTip(), link.getBoundingClientRect());
    }
    doc.addEventListener('selectionchange', function () { if (doc.activeElement === area) { check(); } });
    area.addEventListener('blur', function () { setTimeout(function () { if (tip && doc.activeElement !== area) { tip.hidden = true; } }, 150); });
    area.addEventListener('click', function (e) {
      var link = linkAt(e.target);
      if (link && (e.ctrlKey || e.metaKey)) { e.preventDefault(); openNote(link.getAttribute('data-note-id')); }
    });
  }

  // the notes a plain text or markdown note links to, as chips under the field: a click opens one
  function attachChips(field) {
    var box = $(field.getAttribute('data-note-links'));
    if (!box) { return; }
    function render() {
      var seen = {};
      var html = '';
      var re = /\[([^\]\r\n]*)\]\(note:(\d{1,18})\)/g, m;
      while ((m = re.exec(field.value))) {
        if (seen[m[2]]) { continue; }
        seen[m[2]] = true;
        html += '<a class="mat-tag mn-chip-link" href="' + noteUrl(m[2]) + '">' + icon('note-link') + '<span>' + esc(m[1] || ('Note ' + m[2])) + '</span></a>';
      }
      box.innerHTML = html;
      box.hidden = !html;
    }
    field.addEventListener('input', render);
    render();
  }

  // Enter in the title goes on to the text, like in a notebook
  doc.addEventListener('keydown', function (e) {
    if (e.key !== 'Enter' || e.shiftKey || !e.target.matches || !e.target.matches('input[name=Title]')) { return; }
    var next = $('.mn-editor__area:not([hidden])') || $('textarea#TextContent');
    if (!next) { return; }
    e.preventDefault();
    next.focus();
  });

  $$('[data-editor]').forEach(setupEditor);
  $$('textarea[data-note-links]').forEach(function (t) { attachPicker(t); attachChips(t); });
})();
