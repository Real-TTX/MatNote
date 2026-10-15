/* MatNotes: saves a text note while it is typed (Notes/Edit, the form with data-autosave).

   After a short pause without typing the form is posted to the handler "Autosave" of the page, which answers with JSON: { ok, id, stamp, time }.
   - A new note is created by the first save that has a title; the page then continues as the edit page of that note (hidden Id, address ?id=).
   - The stamp says which version of the note the page has seen: another window or person that saved in the meantime makes the server answer 409, and the page stops
     saving (a save by hand still works: the person decides).
   - Whatever is not yet saved is sent when the page is left (a link, the other mode of the pane, another note in the list, closing the window). */
(function () {
  'use strict';

  var form = document.querySelector('form[data-autosave]');
  if (!form) { return; }

  var DELAY = 1200;
  var status = document.querySelector('[data-autosave-status]');
  var timer = null;
  var busy = false;
  var again = false;
  var stopped = false;
  var unsaved = false;

  function show(text, icon, tone) {
    if (!status) { return; }
    status.querySelector('span').textContent = text;
    var use = status.querySelector('use');
    if (use) { use.setAttribute('href', '#mat-i-' + icon); }
    status.classList.toggle('is-error', tone === 'error');
  }

  function isNew() {
    var id = form.elements.Id;
    return !id || !id.value;
  }

  function ready() {
    if (stopped) { return false; }
    if (!form.elements.Title.value.trim()) { return false; }
    var kind = form.elements.Kind;
    return !(isNew() && kind && kind.value === 'File');
  }

  function address() {
    var url = new URL(location.href);
    url.searchParams.set('handler', 'Autosave');
    return url.toString();
  }

  function schedule() {
    if (stopped) { return; }
    unsaved = true;
    show('Not saved yet ...', 'clock');
    clearTimeout(timer);
    timer = setTimeout(function () { save(false); }, DELAY);
  }

  function created(id) {
    form.elements.Id.value = id;
    var url = new URL(location.href);
    url.searchParams.set('id', id);
    url.searchParams.delete('folder');
    url.searchParams.delete('handler');
    history.replaceState(null, '', url.toString());
    document.body.setAttribute('data-note-id', id);
    // the type is chosen once, with the first save
    if (form.elements.Kind) { form.elements.Kind.disabled = true; }
  }

  function save(leaving) {
    clearTimeout(timer);
    timer = null;
    if (busy) { again = true; return Promise.resolve(); }
    if (!unsaved || !ready()) { return Promise.resolve(); }
    busy = true;
    unsaved = false;
    show('Saving ...', 'progress-play');

    return fetch(address(), { method: 'POST', body: new FormData(form), headers: { Accept: 'application/json' }, credentials: 'same-origin', keepalive: !!leaving })
      .then(function (response) { return response.json().catch(function () { return {}; }).then(function (data) { return { status: response.status, data: data }; }); })
      .then(function (result) {
        var data = result.data;
        if (result.status === 200 && data.ok) {
          if (isNew()) { created(data.id); }
          form.elements.Stamp.value = data.stamp;
          show('Saved at ' + data.time, 'check-circle');
          if (window.mnEmbedTell) { window.mnEmbedTell({ saved: true }); }
        } else if (result.status === 409) {
          stopped = true;
          show('This note was changed somewhere else. Copy your text, then reload the page.', 'alert', 'error');
        } else if (result.status === 422) {
          unsaved = true;
          show(data.error || 'Not saved.', 'alert', 'error');
        } else {
          unsaved = true;
          show('Not saved. Use the Save button.', 'alert', 'error');
        }
      })
      .catch(function () { unsaved = true; show('Not saved: no connection.', 'alert', 'error'); })
      .then(function () {
        busy = false;
        if (again) { again = false; unsaved = true; save(false); }
      });
  }

  // a field can sit outside the form element and still belong to it (the title in the bar of the pane has form="note-form")
  function mine(e) { return e.target && (e.target.form === form || form.contains(e.target)); }
  document.addEventListener('input', function (e) { if (mine(e)) { schedule(); } });
  document.addEventListener('change', function (e) { if (mine(e)) { schedule(); } });
  // the Save button saves everything itself
  form.addEventListener('submit', function () { stopped = true; clearTimeout(timer); });

  // a link of the page (the other mode, back, a linked note): send what is unsaved first, then go
  document.addEventListener('click', function (e) {
    var link = e.target.closest && e.target.closest('a[href]');
    if (!link || !unsaved || stopped || link.target === '_blank' || e.defaultPrevented || e.button || e.ctrlKey || e.metaKey || e.shiftKey) { return; }
    e.preventDefault();
    save(false).then(function () {
      if (link.target === '_top') { window.top.location.href = link.href; } else { location.href = link.href; }
    });
  });

  window.addEventListener('pagehide', function () { if (unsaved && !stopped) { save(true); } });
})();
