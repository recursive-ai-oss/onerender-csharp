// Runs inside a test page in Chrome. It applies the scroll positions the markup asks for (data-scroll="left,top" on any element,
// including <html> for the viewport), then records what a script would read: geometry of every element with an id, and the element
// under each point of a grid. The result is written into <pre id="__probe"> so that `chrome --dump-dom` prints it.
(function () {
  function name(e) { return e ? (e.id || e.tagName.toLowerCase()) : null; }
  window.addEventListener('load', function () {
    document.querySelectorAll('[data-scroll]').forEach(function (e) {
      var p = e.getAttribute('data-scroll').split(',');
      e.scrollLeft = +p[0];
      e.scrollTop = +p[1];
    });
    var out = { viewport: [window.innerWidth, window.innerHeight], elements: {}, points: {} };
    document.querySelectorAll('[id]').forEach(function (e) {
      var r = e.getBoundingClientRect();
      out.elements[e.id] = {
        rect: [r.x, r.y, r.width, r.height],
        rects: e.getClientRects().length,
        offsetParent: name(e.offsetParent),
        offsetLeft: e.offsetLeft, offsetTop: e.offsetTop, offsetWidth: e.offsetWidth, offsetHeight: e.offsetHeight,
        clientLeft: e.clientLeft, clientTop: e.clientTop, clientWidth: e.clientWidth, clientHeight: e.clientHeight,
        scrollWidth: e.scrollWidth, scrollHeight: e.scrollHeight, scrollLeft: e.scrollLeft, scrollTop: e.scrollTop
      };
    });
    for (var y = 8; y < window.innerHeight; y += 16) {
      for (var x = 8; x < window.innerWidth; x += 16) {
        out.points[x + ',' + y] = name(document.elementFromPoint(x, y));
      }
    }
    var pre = document.createElement('pre');
    pre.id = '__probe';
    pre.textContent = JSON.stringify(out);
    document.body.appendChild(pre);
  });
})();
