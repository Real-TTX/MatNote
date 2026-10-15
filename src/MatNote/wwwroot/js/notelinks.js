/* MatNotes: links to other notes inside a note that is read (Notes/View).

   Every link to a note (a.mn-note-link[data-note-id]) in the text gets a small button after it. The button shows the linked note right there, below the paragraph of the
   link, and hides it again. The shown note can have links of its own (the same button), down to three levels; a note that is already above it is not offered again.
   The button "Linked notes inline" of the ribbon (data-inject-all) opens or closes all the links of the note at once.
   The text comes from the page handler "Body" (JSON), which only answers for notes the person may read. */
(function () {
  'use strict';

  var root = document.getElementById('note-body');
  if (!root) { return; }

  var MAX_DEPTH = 3;
  var embed = document.body.getAttribute('data-embed') === 'true';
  var ownId = root.getAttribute('data-note-id');

  function icon(name) { return '<svg class="mat-icon" aria-hidden="true" focusable="false"><use href="#mat-i-' + name + '"></use></svg>'; }
  function esc(s) { return String(s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function viewUrl(id) { return '/Notes/View?id=' + id + (embed ? '&embed=1' : ''); }

  function blockOf(link, container) {
    var block = link;
    while (block.parentNode && block.parentNode !== container) { block = block.parentNode; }
    return block;
  }

  function setOpen(button, open) {
    button.setAttribute('aria-expanded', open ? 'true' : 'false');
    button.innerHTML = icon(open ? 'chevron-down' : 'chevron-right');
    button.classList.toggle('is-open', open);
  }

  function decorate(container, chain) {
    Array.prototype.forEach.call(container.querySelectorAll('a.mn-note-link[data-note-id]'), function (link) {
      var id = link.getAttribute('data-note-id');
      if (link.nextElementSibling && link.nextElementSibling.classList.contains('mn-inject')) { return; }
      if (chain.indexOf(id) !== -1) { return; }
      var button = document.createElement('button');
      button.type = 'button';
      button.className = 'mn-inject';
      button.setAttribute('aria-label', 'Show the note “' + link.textContent.trim() + '” here');
      button.setAttribute('data-tip', 'Show the note here');
      button._link = link;
      button._chain = chain;
      setOpen(button, false);
      link.insertAdjacentElement('afterend', button);
    });
  }

  function show(button) {
    var link = button._link;
    var id = link.getAttribute('data-note-id');
    var container = link.closest('.mn-injected__body') || root;
    var block = blockOf(link, container);
    var holder = document.createElement('section');
    holder.className = 'mn-injected';
    holder.setAttribute('data-for', id);
    holder._block = block;
    holder._button = button;
    holder.innerHTML = '<p class="mat-muted mn-injected__wait">Loading …</p>';
    var after = block;
    while (after.nextElementSibling && after.nextElementSibling._block === block) { after = after.nextElementSibling; }
    after.insertAdjacentElement('afterend', holder);
    button._holder = holder;
    setOpen(button, true);

    return fetch('/Notes/View?handler=Body&id=' + encodeURIComponent(id), { credentials: 'same-origin', headers: { Accept: 'application/json' } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (note) {
        if (!note) { holder.innerHTML = '<p class="mat-muted">This note is not available.</p>'; return; }
        var body = note.html !== null && note.html !== undefined ? note.html : '<p class="mat-muted">This is a file. Open the note to see it.</p>';
        holder.innerHTML = '<header class="mn-injected__head">' + icon(note.icon || 'file-text') + '<a href="' + viewUrl(note.id) + '">' + esc(note.title) + '</a>' +
          '<button type="button" class="mat-btn mat-btn--ghost mat-btn--icon mat-btn--sm" data-inject-close aria-label="Hide this note" data-tip="Hide">' + icon('x') + '</button></header>' +
          '<div class="mn-injected__body mn-doc">' + body + '</div>';
        var inner = holder.querySelector('.mn-injected__body');
        if (button._chain.length < MAX_DEPTH) { decorate(inner, button._chain.concat([String(note.id)])); }
        if (window.mnEmbedLinks) { window.mnEmbedLinks(holder); }
      })
      .catch(function () { holder.innerHTML = '<p class="mat-muted">The note could not be loaded.</p>'; });
  }

  function hide(button) {
    if (button._holder) { button._holder.remove(); button._holder = null; }
    setOpen(button, false);
  }

  function toggle(button) {
    if (button._holder) { hide(button); return Promise.resolve(); }
    return show(button);
  }

  decorate(root, ownId ? [ownId] : []);

  document.addEventListener('click', function (e) {
    var button = e.target.closest && e.target.closest('.mn-inject');
    if (button) { e.preventDefault(); toggle(button); return; }

    var close = e.target.closest && e.target.closest('[data-inject-close]');
    if (close) {
      var holder = close.closest('.mn-injected');
      if (holder && holder._button) { hide(holder._button); holder._button.focus(); }
      return;
    }

    var all = e.target.closest && e.target.closest('[data-inject-all]');
    if (all) {
      var on = all.getAttribute('aria-pressed') !== 'true';
      all.setAttribute('aria-pressed', on ? 'true' : 'false');
      all.classList.toggle('is-on', on);
      Array.prototype.forEach.call(root.querySelectorAll('.mn-inject'), function (b) {
        if (b.closest('.mn-injected')) { return; }
        if (on && !b._holder) { show(b); } else if (!on && b._holder) { hide(b); }
      });
    }
  });
})();
