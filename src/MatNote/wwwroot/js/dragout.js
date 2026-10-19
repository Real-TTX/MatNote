/* MatNotes: drag a note or a folder OUT of the browser (onto the desktop, a folder of the file manager, a mail, a chat).

   A row of the list or a folder of the menu starts a drag that carries what the browser needs to hand over a FILE: Chromium browsers (Edge, Chrome) take the type "DownloadURL"
   (<mime>:<file name>:<address>) and save the file when it is dropped on the desktop or a folder; every browser also gets the address as a link (text/uri-list, text/plain), so a drop
   into a chat or an address bar gives the link to the note.
   - a text note: the note as it is kept (.html, .md or .txt: /Notes/Export?raw=true), which MatNotes takes in again as a text note when the file is dropped on a list
   - a file note (Word, Excel, PDF ...): the file itself
   - a folder: a MatNotes archive (.matnotes.zip) with everything inside, which MatNotes imports again when it is dropped on a list
   The browser asks the server for the file when it is dropped, with the session of the person, so nothing is in the page that the person may not read. */
(function () {
  'use strict';

  var TEXT = { RichText: ['.html', 'text/html'], Markdown: ['.md', 'text/markdown'], PlainText: ['.txt', 'text/plain'] };

  function clean(name) { return (name || 'note').replace(/[\\/:*?"<>|\r\n\t]+/g, ' ').replace(/\s+/g, ' ').trim().replace(/^\.+/, '').slice(0, 120) || 'note'; }

  function absolute(path) { return new URL(path, location.origin).toString(); }

  function offer(e, mime, name, path, view) {
    var dt = e.dataTransfer;
    if (!dt) { return; }
    // the name goes between two colons: it must not have one
    dt.setData('DownloadURL', mime + ':' + clean(name).replace(/:/g, ' ') + ':' + absolute(path));
    dt.setData('text/uri-list', absolute(view));
    dt.setData('text/plain', absolute(view));
    dt.effectAllowed = 'copyLink';
  }

  function ofRow(row) {
    var id = row.getAttribute('data-note-id');
    var type = row.getAttribute('data-type') || '';
    var title = row.getAttribute('data-title') || 'note';
    var view = '/Notes/View?id=' + id;
    if (TEXT[type]) { return { mime: TEXT[type][1], name: clean(title) + TEXT[type][0], path: '/Notes/Export?id=' + id + '&format=Txt&raw=true', view: view }; }
    var fileName = row.getAttribute('data-filename') || title;
    return { mime: 'application/octet-stream', name: fileName, path: '/Notes/File?id=' + id, view: view };
  }

  document.addEventListener('dragstart', function (e) {
    if (!e.target.closest) { return; }
    var row = e.target.closest('[data-split-rows] [data-note-id]');
    if (row) {
      var n = ofRow(row);
      offer(e, n.mime, n.name, n.path, n.view);
      return;
    }
    var link = e.target.closest('.mat-nav a[href^="/Notes?folder="]');
    if (link) {
      var id = /[?&]folder=(\d+)/.exec(link.getAttribute('href'));
      if (!id) { return; }
      var name = (link.querySelector('span') || link).textContent;
      offer(e, 'application/zip', clean(name) + '.matnotes.zip', '/Folders/Export?id=' + id[1] + '&download=true&mode=Archive', '/Notes?folder=' + id[1]);
    }
  });
})();
