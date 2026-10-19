/* MatNotes: duplicate a note or a folder.

   A button with data-duplicate="note:ID" or data-duplicate="folder:ID" (the note ribbon, the folder page), or the event mn:duplicate on the document (the right-click menu), asks the server
   for a copy (POST /Notes/Duplicate or /Folders/Duplicate: a note with "(copy)" after the title, in the same folder, with its tags and attachments; a folder with everything inside it) and then
   shows it: a note opens beside the list (or on its page), a folder is opened. Only what the person owns can be duplicated: the server answers 404 for anything else. */
(function () {
  'use strict';

  var busy = false;
  var embedded = window.parent !== window;

  function toast(message, tone) {
    if (window.MatUi && window.MatUi.toast) { window.MatUi.toast(message, { tone: tone || 'success' }); }
  }

  function token() {
    var field = document.querySelector('input[name="__RequestVerificationToken"]');
    return field ? field.value : '';
  }

  function show(kind, answer) {
    if (kind === 'folder') { location.href = '/Notes?folder=' + answer.id; return; }
    toast('Duplicated as “' + answer.title + '”.');
    if (embedded) {
      // in the pane: the copy opens here, the list beside it gets the new row
      try { window.parent.document.dispatchEvent(new CustomEvent('mn:refresh-list')); } catch (e) { /* the list is not reachable */ }
      location.href = '/Notes/View?id=' + answer.id + '&embed=1';
      return;
    }
    var frame = document.querySelector('[data-split] iframe');
    if (frame && window.matchMedia('(min-width: 1100px)').matches) {
      frame.contentWindow.location.replace('/Notes/View?id=' + answer.id + '&embed=1');
      document.dispatchEvent(new CustomEvent('mn:refresh-list'));
    } else {
      location.href = '/Notes/View?id=' + answer.id;
    }
  }

  function duplicate(kind, id) {
    if (busy || !id) { return; }
    busy = true;
    var body = new URLSearchParams();
    body.set('__RequestVerificationToken', token());
    fetch((kind === 'folder' ? '/Folders/Duplicate' : '/Notes/Duplicate') + '?id=' + encodeURIComponent(id), { method: 'POST', credentials: 'same-origin', body: body })
      .then(function (r) { if (!r.ok) { throw new Error('http ' + r.status); } return r.json(); })
      .then(function (answer) { busy = false; show(kind, answer); })
      .catch(function () { busy = false; toast('It could not be duplicated.', 'danger'); });
  }

  document.addEventListener('click', function (e) {
    var button = e.target.closest && e.target.closest('[data-duplicate]');
    if (!button) { return; }
    e.preventDefault();
    var parts = button.getAttribute('data-duplicate').split(':');
    duplicate(parts[0], parts[1]);
  });

  document.addEventListener('mn:duplicate', function (e) { if (e.detail) { duplicate(e.detail.kind, String(e.detail.id)); } });
})();
