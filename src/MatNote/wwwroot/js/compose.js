/* MatNotes: "Add to a note" - build your own notes from notes you may only read (for example a collection of snippets somebody shared with you).

   A button with data-compose="ID" data-title="Title" (the note page), or the event mn:compose on the document (the right-click menu of the list), opens a dialog: pick one of your own text
   notes, or start a new one, and a LINK to the note is added at its end (POST /Notes/Reference). Nothing is copied: the original stays read-only where it is, and the note you build shows
   its current text where the link is ("Linked notes in the text", and the export). The note picked last is offered first, so a note is built from many snippets in one click each. */
(function () {
  'use strict';

  var KEY = 'mn.compose.target';
  var SVG = 'http://www.w3.org/2000/svg';
  var dialog = null;
  var current = null; // { id, title } of the note that is added
  var timer = null;

  function el(tag, cls, text) {
    var node = document.createElement(tag);
    if (cls) { node.className = cls; }
    if (text !== undefined) { node.textContent = text; }
    return node;
  }

  function icon(name) {
    var svg = document.createElementNS(SVG, 'svg');
    svg.setAttribute('class', 'mat-icon');
    svg.setAttribute('aria-hidden', 'true');
    svg.setAttribute('focusable', 'false');
    var use = document.createElementNS(SVG, 'use');
    use.setAttribute('href', '#mat-i-' + name);
    svg.appendChild(use);
    return svg;
  }

  function toast(message, tone) {
    if (window.MatUi && window.MatUi.toast) { window.MatUi.toast(message, { tone: tone || 'success' }); }
  }

  function token() {
    var field = document.querySelector('input[name="__RequestVerificationToken"]');
    return field ? field.value : '';
  }

  function last() {
    try { return JSON.parse(localStorage.getItem(KEY) || 'null'); } catch (e) { return null; }
  }

  function remember(id, title) {
    try { localStorage.setItem(KEY, JSON.stringify({ id: id, title: title })); } catch (e) { /* ignore */ }
  }

  function add(body, targetTitle) {
    body.set('__RequestVerificationToken', token());
    fetch('/Notes/Reference?id=' + encodeURIComponent(current.id), { method: 'POST', credentials: 'same-origin', body: body })
      .then(function (r) { return r.json().catch(function () { return { ok: false }; }).then(function (a) { return { status: r.status, answer: a }; }); })
      .then(function (r) {
        if (!r.answer.ok) { toast(r.answer.error || 'That did not work.', 'danger'); return; }
        remember(r.answer.id, r.answer.title);
        dialog.close();
        var where = '“' + (r.answer.title || targetTitle) + '”';
        toast(r.answer.already ? 'Already in ' + where + '.' : (r.answer.created ? 'New note ' + where + ' made with the link.' : 'Linked in ' + where + '.'));
        // a list of notes beside shows a new note at once
        document.dispatchEvent(new CustomEvent('mn:refresh-list'));
      })
      .catch(function () { toast('That did not work.', 'danger'); });
  }

  function addTo(id, title) {
    var body = new URLSearchParams();
    body.set('to', id);
    add(body, title);
  }

  function build() {
    var d = el('dialog', 'mat-dialog mat-dialog--sm');
    d.setAttribute('aria-labelledby', 'mn-compose-title');
    var form = el('div', 'mat-dialog__form');

    var head = el('div', 'mat-dialog__head');
    head.appendChild(icon('note-link'));
    var h = el('h2', 'mat-dialog__title', 'Add to a note');
    h.id = 'mn-compose-title';
    head.appendChild(h);
    var close = el('button', 'mat-btn mat-btn--ghost mat-btn--icon mat-dialog__close');
    close.type = 'button';
    close.setAttribute('aria-label', 'Close');
    close.appendChild(icon('x'));
    close.addEventListener('click', function () { d.close(); });
    head.appendChild(close);
    form.appendChild(head);

    var body = el('div', 'mat-dialog__body mat-stack');
    body.appendChild(Object.assign(el('p', 'mat-muted', ''), { id: 'mn-compose-about' }));

    var quick = el('div', 'mn-compose__quick');
    quick.id = 'mn-compose-quick';
    body.appendChild(quick);

    var search = el('div', 'mat-field');
    var label = el('label', 'mat-label', 'One of your notes');
    label.setAttribute('for', 'mn-compose-q');
    var q = el('input', 'mat-input');
    q.type = 'search';
    q.id = 'mn-compose-q';
    q.autocomplete = 'off';
    q.placeholder = 'A title';
    search.appendChild(label);
    search.appendChild(q);
    var list = el('ul', 'mn-lookup__list');
    list.id = 'mn-compose-list';
    list.setAttribute('aria-label', 'Your notes');
    search.appendChild(list);
    body.appendChild(search);

    var fresh = el('div', 'mat-field');
    var nl = el('label', 'mat-label', 'Or a new note');
    nl.setAttribute('for', 'mn-compose-new');
    var row = el('div', 'mn-linkbox');
    var title = el('input', 'mat-input');
    title.id = 'mn-compose-new';
    title.placeholder = 'Title of the new note';
    title.maxLength = 250;
    var make = el('button', 'mat-btn mat-btn--secondary');
    make.type = 'button';
    make.appendChild(icon('file-plus'));
    make.appendChild(el('span', '', 'Create'));
    row.appendChild(title);
    row.appendChild(make);
    fresh.appendChild(nl);
    fresh.appendChild(row);
    body.appendChild(fresh);
    form.appendChild(body);

    var foot = el('div', 'mat-dialog__foot');
    var buttons = el('div', 'mat-btn-row mat-btn-row--plain');
    var cancel = el('button', 'mat-btn mat-btn--secondary', 'Cancel');
    cancel.type = 'button';
    cancel.addEventListener('click', function () { d.close(); });
    buttons.appendChild(cancel);
    foot.appendChild(buttons);
    form.appendChild(foot);
    d.appendChild(form);
    document.body.appendChild(d);

    function create() {
      var name = title.value.trim();
      if (!name) { title.focus(); return; }
      var data = new URLSearchParams();
      data.set('title', name);
      add(data, name);
    }

    make.addEventListener('click', create);
    title.addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); create(); } });
    q.addEventListener('input', function () { clearTimeout(timer); timer = setTimeout(load, 180); });
    list.addEventListener('click', function (e) {
      var pick = e.target.closest && e.target.closest('[data-id]');
      if (pick) { addTo(pick.getAttribute('data-id'), pick.getAttribute('data-title')); }
    });
    quick.addEventListener('click', function (e) {
      var pick = e.target.closest && e.target.closest('[data-id]');
      if (pick) { addTo(pick.getAttribute('data-id'), pick.getAttribute('data-title')); }
    });
    return d;
  }

  function load() {
    var list = dialog.querySelector('#mn-compose-list');
    var q = dialog.querySelector('#mn-compose-q').value.trim();
    fetch('/Notes/Lookup?mine=true&exclude=' + encodeURIComponent(current.id) + '&q=' + encodeURIComponent(q), { credentials: 'same-origin' })
      .then(function (r) { return r.json(); })
      .then(function (items) {
        list.textContent = '';
        if (!items.length) { list.appendChild(el('li', 'mat-muted mn-lookup__none', 'None of your notes matches.')); return; }
        items.forEach(function (n) {
          var li = el('li');
          var b = el('button', 'mat-menu__item');
          b.type = 'button';
          b.setAttribute('data-id', n.id);
          b.setAttribute('data-title', n.title);
          b.appendChild(icon(n.icon));
          b.appendChild(el('span', '', n.title));
          if (n.folder) { b.appendChild(el('small', 'mat-muted', n.folder)); }
          li.appendChild(b);
          list.appendChild(li);
        });
      })
      .catch(function () { list.textContent = ''; list.appendChild(el('li', 'mat-muted mn-lookup__none', 'The notes could not be loaded.')); });
  }

  function open(id, title) {
    if (!id) { return; }
    current = { id: id, title: title || 'this note' };
    if (!dialog) { dialog = build(); }
    dialog.querySelector('#mn-compose-about').textContent = '“' + current.title + '” is linked, not copied: it stays as it is, and your note shows its current text where the link is.';
    var quick = dialog.querySelector('#mn-compose-quick');
    quick.textContent = '';
    var prev = last();
    if (prev && prev.id && String(prev.id) !== String(id)) {
      var b = el('button', 'mat-btn mat-btn--primary');
      b.type = 'button';
      b.setAttribute('data-id', prev.id);
      b.setAttribute('data-title', prev.title || '');
      b.appendChild(icon('note-link'));
      b.appendChild(el('span', '', 'Add to “' + (prev.title || 'the last note') + '”'));
      quick.appendChild(b);
    }
    dialog.querySelector('#mn-compose-q').value = '';
    dialog.querySelector('#mn-compose-new').value = '';
    load();
    if (!dialog.open) { dialog.showModal(); }
    var first = quick.querySelector('button') || dialog.querySelector('#mn-compose-q');
    if (first) { first.focus(); }
  }

  document.addEventListener('click', function (e) {
    var button = e.target.closest && e.target.closest('[data-compose]');
    if (!button) { return; }
    e.preventDefault();
    open(button.getAttribute('data-compose'), button.getAttribute('data-title'));
  });

  document.addEventListener('mn:compose', function (e) { if (e.detail) { open(String(e.detail.id), e.detail.title); } });
})();
