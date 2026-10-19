/* MatNotes: files into the notes list (Notes/Index).

   A file that ends in .matnotes.zip (a folder that was dragged out of MatNotes, or exported as a MatNotes archive) is imported into the open folder instead (POST /Folders/Import).

   "Upload files" in the head and files dropped on the list make one note per file in the folder that is open: a Word, Excel, PDF or other file is kept as a file note, a text file
   (.txt, .md, .html) becomes a text note, as the "Upload a file" type of a new note does. It is the same request as that form (POST /Notes/Edit), one file after the other.
   Files dropped into the note at the right (the pane) are attachments of that note instead: the page in the pane handles those (attachments.js). */
(function () {
  'use strict';

  var zone = document.querySelector('[data-file-drop]');
  if (!zone || zone.getAttribute('data-file-drop') !== 'true') { return; }
  var input = document.querySelector('[data-upload-input]');
  var folder = zone.getAttribute('data-folder') || '';
  var split = document.querySelector('[data-split]');
  var frame = split && split.querySelector('iframe');
  var busy = false;

  function toast(message, tone) {
    if (window.MatUi && window.MatUi.toast) { window.MatUi.toast(message, { tone: tone || 'success' }); }
  }

  function token() {
    var field = document.querySelector('input[name="__RequestVerificationToken"]');
    return field ? field.value : '';
  }

  function titleOf(name) {
    var dot = name.lastIndexOf('.');
    var title = (dot > 0 ? name.slice(0, dot) : name).trim();
    return (title || name || 'File').slice(0, 250);
  }

  // one note from one file; the answer is the page of the new note (the request is redirected to it): its address has the id
  function create(file) {
    var body = new FormData();
    body.append('__RequestVerificationToken', token());
    body.append('Kind', 'File');
    body.append('Title', titleOf(file.name));
    body.append('Folder', folder);
    body.append('Content', '');
    body.append('Tags', '');
    body.append('Favorite', 'false');
    body.append('Stamp', '0');
    body.append('Upload', file, file.name);
    return fetch('/Notes/Edit' + (folder ? '?folder=' + encodeURIComponent(folder) : ''), { method: 'POST', credentials: 'same-origin', body: body }).then(function (r) {
      var id = /[?&]id=(\d+)/.exec(r.url);
      if (!r.ok || !id) { throw new Error('not saved'); }
      return id[1];
    });
  }

  // a MatNotes archive: the import page does the work; its answer says what came in (or why not)
  function importArchive(file) {
    var body = new FormData();
    body.append('__RequestVerificationToken', token());
    body.append('Into', folder);
    body.append('Archive', file, file.name);
    return fetch('/Folders/Import', { method: 'POST', credentials: 'same-origin', body: body }).then(function (r) { return r.text(); }).then(function (html) {
      var page = new DOMParser().parseFromString(html, 'text/html');
      var done = page.querySelector('.mat-alert--success');
      if (!done) { var error = page.querySelector('.mat-error'); throw new Error(error ? error.textContent.trim() : 'not imported'); }
      return done.textContent.replace(/\s+/g, ' ').replace('Open the folder', '').replace('Open your notes', '').trim();
    });
  }

  function upload(files) {
    files = Array.prototype.slice.call(files || []);
    if (!files.length || busy) { return; }
    busy = true;
    zone.classList.add('is-busy');
    var made = [];
    var failed = [];
    var imported = [];
    files.reduce(function (chain, file) {
      return chain.then(function () {
        if (/\.matnotes\.zip$/i.test(file.name)) {
          return importArchive(file).then(function (message) { imported.push(message); }, function (error) { failed.push(file.name + (error && error.message ? ' (' + error.message + ')' : '')); });
        }

        return create(file).then(function (id) { made.push(id); }, function () { failed.push(file.name); });
      });
    }, Promise.resolve()).then(function () {
      busy = false;
      zone.classList.remove('is-busy');
      if (failed.length) { toast('Not saved (too big, empty or not allowed): ' + failed.join(', '), 'danger'); }
      imported.forEach(function (message) { toast(message); });
      if (!made.length) { if (imported.length) { location.reload(); } return; }
      toast(made.length === 1 ? 'Note created from the file.' : made.length + ' notes created from the files.');
      if (frame && window.matchMedia('(min-width: 1100px)').matches) {
        // the last one opens beside the list; the list picks up the new rows when the pane reports it
        frame.contentWindow.location.replace('/Notes/View?id=' + made[made.length - 1] + '&embed=1');
      } else {
        location.reload();
      }
    });
  }

  if (input) { input.addEventListener('change', function () { upload(input.files); input.value = ''; }); }

  function hasFiles(e) { return e.dataTransfer && Array.prototype.indexOf.call(e.dataTransfer.types || [], 'Files') !== -1; }

  var depth = 0;
  zone.addEventListener('dragenter', function (e) { if (hasFiles(e)) { depth += 1; zone.classList.add('is-drop'); } });
  zone.addEventListener('dragleave', function (e) { if (hasFiles(e)) { depth = Math.max(0, depth - 1); if (!depth) { zone.classList.remove('is-drop'); } } });
  zone.addEventListener('dragover', function (e) { if (hasFiles(e)) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; } });
  zone.addEventListener('drop', function (e) {
    if (!hasFiles(e)) { return; }
    e.preventDefault();
    depth = 0;
    zone.classList.remove('is-drop');
    upload(e.dataTransfer.files);
  });
  // a file dropped beside the list must not make the browser open it
  document.addEventListener('dragover', function (e) { if (hasFiles(e) && !zone.contains(e.target)) { e.preventDefault(); e.dataTransfer.dropEffect = 'none'; } });
  document.addEventListener('drop', function (e) { if (hasFiles(e) && !zone.contains(e.target)) { e.preventDefault(); } });
})();
