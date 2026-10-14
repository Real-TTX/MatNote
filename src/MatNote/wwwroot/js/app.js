/* MatNotes: the few behaviours that only this app has (the toolkit does the rest).

   data-copy-inner="#id"   copy the text of an element as the person reads it (line breaks of paragraphs and list items kept): the "Copy" button of a rich text note.
                           (data-copy="#id" of the toolkit copies the value of a field, which is what plain text and markdown notes use.) */
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
