/* MatNotes: parameters of a note.

   A note can have places that are filled in when it is used: {{name}} or {{name|default}} in its text (a prompt with a place for the text it should work on). The person is asked for the
   values (one field each; what was typed last time is offered again) when the note is
   - copied (the Copy button: the text as it is shown, with the linked notes that are copied in, so a prompt built from snippets asks for all of them at once),
   - exported as a file (the export dialog: the same values go to the server, which writes them into every format),
   - copied to the clipboard from the export dialog.
   A note without placeholders is not touched. window.mnParams is what the other scripts call (app.js: copy to clipboard from the export). */
(function () {
  'use strict';

  var KEY = 'mn.params';
  var SVG = 'http://www.w3.org/2000/svg';
  var PLACEHOLDER = /\{\{\s*([^{}|]{1,40}?)\s*(?:\|([^{}]*))?\}\}/g;
  var dialog = null;

  function names(text) {
    var found = [];
    var seen = {};
    String(text || '').replace(PLACEHOLDER, function (all, name, def) {
      if (!seen[name]) { seen[name] = true; found.push({ name: name, def: def === undefined ? '' : def }); }
      return all;
    });
    return found;
  }

  function apply(text, values) {
    return String(text).replace(PLACEHOLDER, function (all, name, def) {
      if (Object.prototype.hasOwnProperty.call(values, name)) { return values[name]; }
      return def === undefined ? all : def;
    });
  }

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

  function remembered() {
    try { return JSON.parse(localStorage.getItem(KEY) || '{}') || {}; } catch (e) { return {}; }
  }

  function remember(values) {
    var all = remembered();
    Object.keys(values).forEach(function (name) { all[name] = values[name]; });
    // the last forty names are enough
    var keys = Object.keys(all);
    if (keys.length > 40) { keys.slice(0, keys.length - 40).forEach(function (k) { delete all[k]; }); }
    try { localStorage.setItem(KEY, JSON.stringify(all)); } catch (e) { /* ignore */ }
  }

  /** Asks for the values of these parameters: a promise of { name: value }, or null when the person cancels. */
  function ask(list) {
    return new Promise(function (resolve) {
      if (dialog) { dialog.remove(); }
      var d = el('dialog', 'mat-dialog mat-dialog--sm');
      d.setAttribute('aria-labelledby', 'mn-params-title');
      var form = el('form', 'mat-dialog__form');
      form.method = 'dialog';
      var head = el('div', 'mat-dialog__head');
      head.appendChild(icon('edit'));
      var h = el('h2', 'mat-dialog__title', 'Fill in the values');
      h.id = 'mn-params-title';
      head.appendChild(h);
      form.appendChild(head);

      var body = el('div', 'mat-dialog__body mat-stack');
      body.appendChild(el('p', 'mat-muted', list.length === 1 ? 'This note has a place to fill in.' : 'This note has ' + list.length + ' places to fill in.'));
      var last = remembered();
      var fields = list.map(function (p, i) {
        var field = el('div', 'mat-field');
        var label = el('label', 'mat-label', p.name);
        label.setAttribute('for', 'mn-param-' + i);
        var input = el('textarea', 'mat-textarea');
        input.id = 'mn-param-' + i;
        input.rows = 2;
        input.value = Object.prototype.hasOwnProperty.call(last, p.name) ? last[p.name] : p.def;
        if (p.def) { input.placeholder = p.def; }
        field.appendChild(label);
        field.appendChild(input);
        body.appendChild(field);
        return { name: p.name, input: input };
      });
      form.appendChild(body);

      var foot = el('div', 'mat-dialog__foot');
      var row = el('div', 'mat-btn-row mat-btn-row--plain');
      var ok = el('button', 'mat-btn mat-btn--primary');
      ok.type = 'submit';
      ok.appendChild(icon('check'));
      ok.appendChild(el('span', '', 'Continue'));
      var cancel = el('button', 'mat-btn mat-btn--secondary', 'Cancel');
      cancel.type = 'button';
      row.appendChild(ok);
      row.appendChild(cancel);
      foot.appendChild(row);
      form.appendChild(foot);
      d.appendChild(form);
      document.body.appendChild(d);
      dialog = d;

      var done = false;
      function finish(values) { if (done) { return; } done = true; if (d.open) { d.close(); } d.remove(); dialog = null; resolve(values); }
      form.addEventListener('submit', function (e) {
        e.preventDefault();
        var values = {};
        fields.forEach(function (f) { values[f.name] = f.input.value; });
        remember(values);
        finish(values);
      });
      cancel.addEventListener('click', function () { finish(null); });
      d.addEventListener('cancel', function () { finish(null); });
      // Ctrl + Enter in a field continues
      d.addEventListener('keydown', function (e) { if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); form.requestSubmit(); } });
      d.showModal();
      if (fields[0]) { fields[0].input.focus(); fields[0].input.select(); }
    });
  }

  /** The values for a text: {} when it has no parameters, the answers of the person, or null when they cancel. */
  function collect(text) {
    var list = names(text);
    return list.length ? ask(list) : Promise.resolve({});
  }

  /** The text with its parameters filled in (asks first); the text itself when it has none, null when the person cancels. */
  function fill(text) {
    return collect(text).then(function (values) { return values === null ? null : apply(text, values); });
  }

  window.mnParams = { names: names, apply: apply, collect: collect, fill: fill };

  function copy(text) {
    var done = function () { toast('Copied to the clipboard.'); };
    var failed = function () { toast('The text could not be copied.', 'danger'); };
    if (navigator.clipboard && window.isSecureContext) { navigator.clipboard.writeText(text).then(done, failed); return; }
    var area = el('textarea', 'mat-sr-only');
    area.value = text;
    document.body.appendChild(area);
    area.select();
    try { document.execCommand('copy') ? done() : failed(); } catch (e) { failed(); } finally { area.remove(); }
  }

  // the Copy buttons (the toolkit's data-copy, and data-copy-inner of app.js): a text with placeholders is filled first; any other text goes its way
  function textOf(button) {
    var inner = button.getAttribute('data-copy-inner');
    var ref = inner || button.getAttribute('data-copy');
    if (!ref) { return ''; }
    if (ref.charAt(0) !== '#') { return inner ? '' : ref; }
    var source = document.querySelector(ref);
    if (!source) { return ''; }
    return inner ? (source.innerText || source.textContent || '').trim() : (source.value !== undefined ? source.value : (source.textContent || ''));
  }

  document.addEventListener('click', function (e) {
    var button = e.target.closest && e.target.closest('[data-copy-inner], [data-copy]');
    if (!button || button.hasAttribute('data-export-copy')) { return; }
    var text = textOf(button);
    if (!text || !names(text).length) { return; }
    e.preventDefault();
    e.stopImmediatePropagation();
    var inner = button.getAttribute('data-copy-inner');
    collect(text).then(function (values) {
      if (values === null) { return; }
      if (inner && window.mnCopyRich) {
        // the design goes along: the values are written into the text of a copy of the page
        var copyOf = document.querySelector(inner).cloneNode(true);
        var walker = document.createTreeWalker(copyOf, NodeFilter.SHOW_TEXT);
        var node;
        while ((node = walker.nextNode())) { node.nodeValue = apply(node.nodeValue, values); }
        window.mnCopyRich(copyOf.innerHTML, apply(text, values)).then(function () { toast('Copied to the clipboard.'); }, function () { toast('The text could not be copied.', 'danger'); });
        return;
      }
      copy(apply(text, values));
    });
  }, true);

  // the export dialog of a text note: ask for the values, send them along (p_name=value), then download as usual
  document.addEventListener('submit', function (e) {
    var form = e.target;
    if (!form.matches || !form.matches('form[action$="/Notes/Export"], form[action*="/export/"]') || !form.querySelector('[name="linked"]') || form.hasAttribute('data-params-done')) { return; }
    e.preventDefault();
    var query = new URLSearchParams();
    query.set('format', 'Txt');
    Array.prototype.forEach.call(form.querySelectorAll('input[type=hidden], input[type=checkbox]:checked'), function (field) { if (field.name !== 'attachments') { query.set(field.name, field.value); } });
    var send = function () {
      form.setAttribute('data-params-done', '');
      form.submit();
      setTimeout(function () {
        form.removeAttribute('data-params-done');
        Array.prototype.forEach.call(form.querySelectorAll('[data-param-field]'), function (f) { f.remove(); });
      }, 500);
    };
    fetch(form.getAttribute('action') + '?' + query.toString(), { credentials: 'same-origin' }).then(function (r) { return r.ok ? r.text() : ''; }).then(collect).then(function (values) {
      if (values === null) { return; }
      Object.keys(values).forEach(function (name) {
        var input = document.createElement('input');
        input.type = 'hidden';
        input.name = 'p_' + name;
        input.value = values[name];
        input.setAttribute('data-param-field', '');
        form.appendChild(input);
      });
      send();
    }).catch(send);
  }, true);
})();
