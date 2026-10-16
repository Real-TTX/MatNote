/* MatNotes: the right-click menu of a note (a row of the list) and of a folder (an entry of the menu at the left).

   What the menu offers is what the page already offers somewhere else (open, edit, share, new subfolder ...): it only puts it under the pointer. Nothing is decided here;
   the server checks every address as before. A row says what the person may do with data-own, data-can-edit, data-fav and data-file; a folder entry is a link to /Notes?folder=ID
   in the menu (only the person's own folders are there).
   The menu is the toolkit's .mat-menu. Arrow keys, Home/End, Escape; the menu key or Shift+F10 on a focused row opens it too. A note of the list opens in the pane (data-open-pane),
   as a click would. Shift + right click leaves the menu of the browser alone. */
(function () {
  'use strict';

  var SVG = 'http://www.w3.org/2000/svg';
  var menu = null;
  var opener = null;

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

  function item(spec) {
    var el = document.createElement(spec.href ? 'a' : 'button');
    el.className = 'mat-menu__item';
    el.setAttribute('role', 'menuitem');
    if (spec.href) { el.href = spec.href; if (spec.pane) { el.setAttribute('data-open-pane', ''); } } else { el.type = 'button'; }
    el.appendChild(icon(spec.icon));
    var text = document.createElement('span');
    text.textContent = spec.text;
    el.appendChild(text);
    if (spec.run) { el.addEventListener('click', function (e) { e.preventDefault(); close(); spec.run(); }); }
    return el;
  }

  function close() {
    if (!menu) { return; }
    menu.remove();
    menu = null;
    if (opener && opener.focus && document.contains(opener)) { opener.focus({ preventScroll: true }); }
    opener = null;
  }

  function toast(message, tone) {
    if (window.MatUi && window.MatUi.toast) { window.MatUi.toast(message, { tone: tone || 'success' }); }
  }

  function copy(text) {
    var done = function () { toast('Copied to the clipboard.'); };
    var failed = function () { toast('The text could not be copied.', 'danger'); };
    if (navigator.clipboard && window.isSecureContext) { navigator.clipboard.writeText(text).then(done, failed); return; }
    var area = document.createElement('textarea');
    area.value = text;
    area.className = 'mat-sr-only';
    document.body.appendChild(area);
    area.select();
    try { document.execCommand('copy') ? done() : failed(); } catch (e) { failed(); } finally { area.remove(); }
  }

  function token() {
    var field = document.querySelector('input[name="__RequestVerificationToken"]');
    return field ? field.value : '';
  }

  function favorite(id, on) {
    var body = new URLSearchParams();
    body.set('__RequestVerificationToken', token());
    fetch('/Notes/View?handler=Favorite&id=' + encodeURIComponent(id), { method: 'POST', credentials: 'same-origin', body: body })
      .then(function (r) { if (!r.ok) { throw new Error('failed'); } toast(on ? 'Removed from your favorites.' : 'Added to your favorites.'); document.dispatchEvent(new CustomEvent('mn:refresh-list')); })
      .catch(function () { toast('That did not work.', 'danger'); });
  }

  // an attribute that is there but empty means no (the server writes data-x="" for a no)
  function flag(row, name) { return row.getAttribute(name) === 'true'; }

  function noteItems(row) {
    var id = row.getAttribute('data-note-id');
    var own = flag(row, 'data-own');
    var items = [{ icon: 'eye', text: 'Open', href: '/Notes/View?id=' + id, pane: true }];
    if (flag(row, 'data-can-edit')) { items.push({ icon: 'edit', text: 'Edit', href: '/Notes/Edit?id=' + id, pane: true }); }
    if (own) { items.push({ icon: 'share', text: 'Share', href: '/Shares/Edit?noteId=' + id, pane: true }); }
    items.push('-');
    if (own) {
      var on = flag(row, 'data-fav');
      items.push({ icon: 'star', text: on ? 'Remove favorite' : 'Favorite', run: function () { favorite(id, on); } });
    }
    items.push({ icon: 'layers', text: 'Add to a note', run: function () { document.dispatchEvent(new CustomEvent('mn:compose', { detail: { id: id, title: row.getAttribute('data-title') || 'Note' } })); } });
    items.push({ icon: 'note-link', text: 'Copy link to this note', run: function () { copy('[' + (row.getAttribute('data-title') || 'Note') + '](note:' + id + ')'); } });
    if (flag(row, 'data-file')) { items.push({ icon: 'download', text: 'Download the file', href: '/Notes/File?id=' + id }); }
    return items;
  }

  function folderItems(id) {
    return [
      { icon: 'folder', text: 'Open', href: '/Notes?folder=' + id },
      { icon: 'file-plus', text: 'New note here', href: '/Notes/Edit?folder=' + id, pane: true },
      { icon: 'folder-plus', text: 'New subfolder', href: '/Folders/Edit?parent=' + id },
      '-',
      { icon: 'edit', text: 'Edit folder', href: '/Folders/Edit?id=' + id },
      { icon: 'share', text: 'Share', href: '/Shares/Edit?folderId=' + id },
      { icon: 'upload', text: 'Export as zip', href: '/Folders/Export?id=' + id },
      '-',
      { icon: 'settings', text: 'Manage all folders', href: '/Folders' },
    ];
  }

  function show(items, x, y, label) {
    close();
    menu = document.createElement('div');
    menu.className = 'mat-menu mn-ctx';
    menu.setAttribute('role', 'menu');
    menu.setAttribute('aria-label', label);
    items.forEach(function (spec) {
      if (spec === '-') { var sep = document.createElement('div'); sep.className = 'mat-menu__sep'; sep.setAttribute('role', 'separator'); menu.appendChild(sep); return; }
      menu.appendChild(item(spec));
    });
    document.body.appendChild(menu);
    var w = menu.offsetWidth;
    var h = menu.offsetHeight;
    menu.style.left = Math.max(8, Math.min(x, window.innerWidth - w - 8)) + 'px';
    menu.style.top = Math.max(8, Math.min(y, window.innerHeight - h - 8)) + 'px';
    menu.style.transformOrigin = 'top left';
    var first = menu.querySelector('.mat-menu__item');
    if (first) { first.focus({ preventScroll: true }); }
  }

  // what a right click (or the menu key) on this element opens: { items, label } or null
  function targetOf(el) {
    var row = el.closest('[data-split-rows] [data-note-id]');
    if (row) { return { items: noteItems(row), label: 'Note', anchor: row.querySelector('a') || row }; }
    var link = el.closest('.mat-nav a[href^="/Notes?folder="]');
    if (link) {
      var id = /[?&]folder=(\d+)/.exec(link.getAttribute('href'));
      if (id) { return { items: folderItems(id[1]), label: 'Folder', anchor: link }; }
    }
    return null;
  }

  document.addEventListener('contextmenu', function (e) {
    if (e.shiftKey || !e.target.closest) { return; }
    var target = targetOf(e.target);
    if (!target) { return; }
    e.preventDefault();
    opener = target.anchor;
    show(target.items, e.clientX, e.clientY, target.label);
  });

  document.addEventListener('keydown', function (e) {
    if (menu) {
      var items = Array.prototype.slice.call(menu.querySelectorAll('.mat-menu__item'));
      var at = items.indexOf(document.activeElement);
      if (e.key === 'Escape') { e.preventDefault(); close(); }
      else if (e.key === 'ArrowDown') { e.preventDefault(); items[(at + 1) % items.length].focus(); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); items[(at <= 0 ? items.length : at) - 1].focus(); }
      else if (e.key === 'Home') { e.preventDefault(); items[0].focus(); }
      else if (e.key === 'End') { e.preventDefault(); items[items.length - 1].focus(); }
      else if (e.key === 'Tab') { close(); }
      return;
    }
    if (e.key === 'ContextMenu' || (e.key === 'F10' && e.shiftKey)) {
      var target = e.target.closest && targetOf(e.target);
      if (!target) { return; }
      e.preventDefault();
      var box = target.anchor.getBoundingClientRect();
      opener = target.anchor;
      show(target.items, box.left + 24, box.bottom - 4, target.label);
    }
  });

  // a click anywhere else, scrolling or a change of the window closes it; a click on an entry runs it first (the entry closes the menu itself or the page unloads)
  document.addEventListener('mousedown', function (e) { if (menu && !menu.contains(e.target)) { close(); } }, true);
  document.addEventListener('click', function (e) { if (menu && e.target.closest && e.target.closest('.mn-ctx a')) { setTimeout(close, 0); } });
  window.addEventListener('blur', close);
  window.addEventListener('resize', close);
  document.addEventListener('scroll', close, true);
})();
