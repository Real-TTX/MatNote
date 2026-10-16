/* MatNotes: the attachments of a note (Pages/Shared/_Attachments).

   - "Add files" (a file field) and dropping files anywhere on the page upload them to the note at once (POST /Notes/Attachment?handler=Upload, JSON back); the list is filled from
     the answer. A note that is not saved yet has no address: the person is asked for a title first (autosave creates the note, the section appears, then the files go).
   - "x" removes one after a question (POST ?handler=Delete).
   The server decides who may add and remove; this script only asks. */
(function () {
  'use strict';

  var section = document.querySelector('[data-attach]');
  if (!section) { return; }
  var list = section.querySelector('[data-attach-list]');
  var count = section.querySelector('[data-attach-count]');
  var template = section.querySelector('[data-attach-template]');
  var input = section.querySelector('[data-attach-input]');
  var canEdit = section.getAttribute('data-can-edit') === 'true';
  var busy = 0;

  function toast(message, tone) {
    if (window.MatUi && window.MatUi.toast) { window.MatUi.toast(message, { tone: tone || 'success' }); }
  }

  function noteId() { return section.getAttribute('data-note-id') || document.body.getAttribute('data-note-id') || ''; }

  function token() {
    var field = section.querySelector('input[name="__RequestVerificationToken"]');
    return field ? field.value : '';
  }

  function sync() {
    var n = list.children.length;
    if (count) { count.textContent = String(n); }
    section.classList.toggle('is-empty', n === 0);
  }

  function add(file) {
    var li = template.content.firstElementChild.cloneNode(true);
    li.setAttribute('data-file-id', file.id);
    var link = li.querySelector('.mn-attach__name');
    link.href = file.url;
    link.textContent = file.name;
    li.querySelector('.mn-attach__size').textContent = file.size;
    var use = li.querySelector('use');
    if (use) { use.setAttribute('href', '#mat-i-' + file.icon); }
    var remove = li.querySelector('[data-attach-remove]');
    if (remove) { remove.setAttribute('aria-label', 'Remove ' + file.name); }
    list.appendChild(li);
  }

  function upload(files) {
    files = Array.prototype.slice.call(files || []);
    if (!files.length || !canEdit) { return; }
    var id = noteId();
    if (!id) {
      toast('Give the note a title first: it is saved while you type, then you can attach files.', 'danger');
      return;
    }
    var body = new FormData();
    body.append('__RequestVerificationToken', token());
    files.forEach(function (f) { body.append('files', f, f.name); });
    busy += 1;
    section.classList.add('is-busy');
    fetch('/Notes/Attachment?handler=Upload&id=' + encodeURIComponent(id), { method: 'POST', credentials: 'same-origin', body: body })
      .then(function (r) { if (!r.ok) { throw new Error('http ' + r.status); } return r.json(); })
      .then(function (answer) {
        (answer.added || []).forEach(add);
        sync();
        (answer.errors || []).forEach(function (message) { toast(message, 'danger'); });
        if ((answer.added || []).length) { toast(answer.added.length === 1 ? 'File attached.' : answer.added.length + ' files attached.'); }
      })
      .catch(function () { toast('The files could not be attached.', 'danger'); })
      .then(function () { busy -= 1; if (!busy) { section.classList.remove('is-busy'); } });
  }

  if (input) {
    input.addEventListener('change', function () { upload(input.files); input.value = ''; });
  }

  section.addEventListener('click', function (e) {
    var button = e.target.closest && e.target.closest('[data-attach-remove]');
    if (!button) { return; }
    var li = button.closest('[data-file-id]');
    var name = li.querySelector('.mn-attach__name').textContent;
    var go = function () {
      var body = new URLSearchParams();
      body.set('__RequestVerificationToken', token());
      fetch('/Notes/Attachment?handler=Delete&id=' + encodeURIComponent(noteId()) + '&file=' + encodeURIComponent(li.getAttribute('data-file-id')), { method: 'POST', credentials: 'same-origin', body: body })
        .then(function (r) { if (!r.ok) { throw new Error('http ' + r.status); } li.remove(); sync(); toast('Attachment removed.'); })
        .catch(function () { toast('The attachment could not be removed.', 'danger'); });
    };
    if (window.MatUi && window.MatUi.confirm) {
      window.MatUi.confirm('Remove “' + name + '” from this note? The file is deleted.', { danger: true, ok: 'Remove', title: 'Remove attachment' }).then(function (ok) { if (ok) { go(); } });
    } else { go(); }
  });

  // dropping files anywhere on the page (the file field of a new file note takes its own file)
  if (!canEdit) { return; }
  var depth = 0;

  function hasFiles(e) { return e.dataTransfer && Array.prototype.indexOf.call(e.dataTransfer.types || [], 'Files') !== -1; }

  function over(on) { document.documentElement.classList.toggle('mn-dropping', on); }

  document.addEventListener('dragenter', function (e) { if (hasFiles(e)) { depth += 1; over(true); } });
  document.addEventListener('dragleave', function (e) { if (hasFiles(e)) { depth = Math.max(0, depth - 1); if (!depth) { over(false); } } });
  document.addEventListener('dragover', function (e) { if (hasFiles(e)) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; } });
  document.addEventListener('drop', function (e) {
    if (!hasFiles(e)) { return; }
    e.preventDefault();
    depth = 0;
    over(false);
    if (e.target.closest && e.target.closest('input[type=file]')) { return; }
    upload(e.dataTransfer.files);
  });
})();
