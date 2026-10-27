#!/usr/bin/env python3
"""Generates the document templates (invoice, report, statement, receipt, certificate, email).

Every page is plain HTML with one stylesheet beside it. The text is made from a small word list by a seeded
generator, so the output is the same on every run:  python3 generate.py  (from this folder).
"""
import os
import random

HERE = os.path.dirname(os.path.abspath(__file__))
WORDS = ("account balance carrier charge client contract delivery document due estimate filing fiscal forecast "
         "freight general handling income invoice ledger license maintenance margin notice order payment period "
         "policy premium project quarter rate receipt reference region report revenue schedule service shipment "
         "statement summary supply support tax term total transfer unit usage vendor warranty").split()
LOGO = ("data:image/svg+xml;utf8,%3Csvg xmlns='http://www.w3.org/2000/svg' width='48' height='48' viewBox='0 0 48 48'%3E"
        "%3Crect width='48' height='48' rx='8' fill='%231f4e79'/%3E%3Cpath d='M12 34V14h8c6 0 9 3 9 7s-3 7-9 7h-3v6z' fill='%23fff'/%3E%3C/svg%3E")


def rnd(name):
    return random.Random("onerender-templates-" + name)


def words(r, n):
    return " ".join(r.choice(WORDS) for _ in range(n))


def sentence(r, lo=8, hi=18):
    s = words(r, r.randint(lo, hi))
    return s[0].upper() + s[1:] + "."


def paragraph(r, lo=3, hi=6):
    return " ".join(sentence(r) for _ in range(r.randint(lo, hi)))


def money(v):
    return "{:,.2f}".format(v)


def write(name, html, css, root=HERE):
    d = os.path.join(root, name)
    os.makedirs(d, exist_ok=True)
    with open(os.path.join(d, "index.html"), "w", encoding="utf-8", newline="\n") as f:
        f.write(html)
    if css is not None:
        with open(os.path.join(d, "styles.css"), "w", encoding="utf-8", newline="\n") as f:
            f.write(css)


def head(title):
    return ('<!DOCTYPE html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n<title>%s</title>\n'
            '<meta name="viewport" content="width=device-width, initial-scale=1">\n'
            '<link rel="stylesheet" href="styles.css">\n</head>\n' % title)


# ---------------------------------------------------------------- invoice

def invoice():
    r = rnd("invoice")
    rows = []
    sub = 0.0
    for i in range(1, 15):
        qty = r.randint(1, 40)
        price = r.randint(500, 45000) / 100
        amt = qty * price
        sub += amt
        rows.append('<tr><td class="num">%d</td><td><strong>%s</strong><br><span class="muted">%s</span></td>'
                    '<td class="num">%d</td><td class="num">%s</td><td class="num">%s</td></tr>'
                    % (i, words(r, 2).title(), sentence(r, 5, 9), qty, money(price), money(amt)))
    tax = sub * 0.0825
    html = head("Invoice INV-2026-0418") + """<body>
<div class="sheet">
<header class="top">
  <img class="logo" src="%s" width="48" height="48" alt="Northwind Supply logo">
  <div class="from">
    <h1>Northwind Supply Co.</h1>
    <p>1200 Harbor Street, Suite 400<br>Portland, OR 97209<br>billing@northwind.example</p>
  </div>
  <div class="meta">
    <h2>INVOICE</h2>
    <dl>
      <dt>Number</dt><dd>INV-2026-0418</dd>
      <dt>Issued</dt><dd>October 26, 2026</dd>
      <dt>Due</dt><dd>November 25, 2026</dd>
      <dt>Terms</dt><dd>Net 30</dd>
    </dl>
  </div>
</header>
<section class="parties">
  <div><h3>Bill to</h3><p>Fabrikam Logistics LLC<br>77 Meridian Way<br>Seattle, WA 98101</p></div>
  <div><h3>Ship to</h3><p>Fabrikam Logistics &mdash; Dock 6<br>4100 Terminal Road<br>Tacoma, WA 98421</p></div>
  <div><h3>Purchase order</h3><p>PO-88213<br>Contact: A. Okafor</p></div>
</section>
<table class="lines">
<thead><tr><th class="num">#</th><th>Description</th><th class="num">Qty</th><th class="num">Unit price</th><th class="num">Amount</th></tr></thead>
<tbody>
%s
</tbody>
</table>
<table class="totals">
<tr><th>Subtotal</th><td>%s</td></tr>
<tr><th>Sales tax (8.25%%)</th><td>%s</td></tr>
<tr><th>Shipping</th><td>0.00</td></tr>
<tr class="grand"><th>Total due (USD)</th><td>%s</td></tr>
</table>
<section class="notes">
<h3>Payment instructions</h3>
<p>Pay by bank transfer to account <code>0041-5521-9087</code>, routing <code>123000220</code>, quoting the invoice number. Late payments accrue <em>1.5%% per month</em>. %s</p>
<ul><li>Questions about this invoice: <a href="mailto:billing@northwind.example">billing@northwind.example</a></li><li>Disputes must be raised within 14 days.</li></ul>
</section>
<footer><p>Northwind Supply Co. &middot; Tax ID 93-1234567 &middot; Page 1 of 1</p></footer>
</div>
</body>
</html>
""" % (LOGO, "\n".join(rows), money(sub), money(tax), money(sub + tax), paragraph(r, 2, 3))
    css = """* { box-sizing: border-box; }
body { margin: 0; background: #eef0f3; color: #1c2430; font: 14px/1.45 Arial, Helvetica, sans-serif; }
.sheet { width: 794px; max-width: 100%; margin: 0 auto; padding: 48px 56px; background: #fff; }
.top { display: flex; align-items: flex-start; gap: 20px; border-bottom: 3px solid #1f4e79; padding-bottom: 20px; }
.logo { flex: none; }
.from { flex: 1; }
.from h1 { margin: 0 0 4px; font-size: 22px; color: #1f4e79; }
.from p { margin: 0; color: #4a5568; }
.meta { text-align: right; }
.meta h2 { margin: 0 0 8px; font-size: 26px; letter-spacing: 0.12em; color: #1f4e79; }
.meta dl { display: grid; grid-template-columns: auto auto; gap: 2px 14px; margin: 0; }
.meta dt { color: #6b7280; text-align: right; }
.meta dd { margin: 0; font-weight: bold; text-align: right; }
.parties { display: flex; gap: 24px; margin: 24px 0; }
.parties > div { flex: 1; }
.parties h3, .notes h3 { margin: 0 0 6px; font-size: 11px; text-transform: uppercase; letter-spacing: 0.08em; color: #6b7280; }
.parties p { margin: 0; }
table { border-collapse: collapse; width: 100%; }
.lines th { background: #1f4e79; color: #fff; text-align: left; padding: 8px 10px; font-size: 12px; }
.lines td { padding: 8px 10px; border-bottom: 1px solid #d9dee5; vertical-align: top; }
.lines tbody tr:nth-child(even) td { background: #f6f8fa; }
.num { text-align: right !important; white-space: nowrap; }
.muted { color: #6b7280; font-size: 12px; }
.totals { width: 320px; margin: 20px 0 0 auto; }
.totals th { text-align: left; font-weight: normal; padding: 5px 10px; }
.totals td { text-align: right; padding: 5px 10px; }
.totals .grand th, .totals .grand td { font-size: 16px; font-weight: bold; border-top: 2px solid #1f4e79; padding-top: 9px; }
.notes { margin-top: 28px; }
.notes p { margin: 0 0 8px; }
.notes code { font-family: "Courier New", monospace; background: #f1f3f5; padding: 1px 4px; }
footer { margin-top: 32px; padding-top: 12px; border-top: 1px solid #d9dee5; text-align: center; color: #6b7280; font-size: 11px; }
"""
    write("invoice", html, css)


# ---------------------------------------------------------------- invoice with spans

def invoice_spanned():
    """The invoice with spanned cells: a row-spanning cell in the body and the totals as a footer whose labels span four columns."""
    import re
    with open(os.path.join(HERE, "invoice", "index.html"), encoding="utf-8") as f:
        html = f.read()
    with open(os.path.join(HERE, "invoice", "styles.css"), encoding="utf-8") as f:
        css = f.read()
    html = html.replace("Invoice INV-2026-0418", "Invoice INV-2026-0418 with spans", 1)
    # Lines 1 and 2 share one number cell.
    html = html.replace('<tr><td class="num">1</td>', '<tr><td class="num" rowspan="2">1&ndash;2</td>', 1)
    html = html.replace('<tr><td class="num">2</td>', '<tr>', 1)
    totals = re.search(r'<table class="totals">(.*?)</table>', html, re.S)
    foot = []
    for m in re.finditer(r'<tr( class="grand")?><th>(.*?)</th><td>(.*?)</td></tr>', totals.group(1)):
        foot.append('<tr%s><th colspan="4" scope="row" class="num">%s</th><td class="num">%s</td></tr>' % (m.group(1) or "", m.group(2), m.group(3)))
    html = html.replace(totals.group(0), "")
    html = html.replace("</tbody>", "</tbody>\n<tfoot>\n" + "\n".join(foot) + "\n</tfoot>", 1)
    css += "tfoot th, tfoot td { padding: 6px 8px; border-top: 1px solid #ccc; }\ntfoot .grand th, tfoot .grand td { font-weight: bold; border-top: 2px solid #333; }\n"
    write("invoice-spanned", html, css)


# ---------------------------------------------------------------- statement

def statement():
    r = rnd("statement")
    bal = 12480.55
    rows = []
    for i in range(1, 181):
        amt = r.randint(300, 250000) / 100
        credit = r.random() < 0.28
        bal += amt if credit else -amt
        day = 1 + (i * 31) // 181
        rows.append('<tr class="%s"><td>Oct %02d</td><td>%s</td><td class="ref">%06d</td><td class="num">%s</td><td class="num">%s</td><td class="num">%s</td></tr>'
                    % ("cr" if credit else "dr", day, words(r, r.randint(2, 5)).title(), r.randint(1000, 999999),
                       "" if credit else money(amt), money(amt) if credit else "", money(bal)))
    html = head("Account statement, October 2026") + """<body>
<main>
<header>
  <h1>Meridian Credit Union</h1>
  <p class="sub">Checking account statement &mdash; October 1 to October 31, 2026</p>
</header>
<section class="summary">
  <div class="card"><span>Opening balance</span><strong>$12,480.55</strong></div>
  <div class="card"><span>Total credits</span><strong>$%s</strong></div>
  <div class="card"><span>Total debits</span><strong>$%s</strong></div>
  <div class="card"><span>Closing balance</span><strong>$%s</strong></div>
</section>
<p class="addr">Jordan A. Whitfield<br>918 Elm Court<br>Madison, WI 53703</p>
<h2>Transactions</h2>
<table>
<caption>Posted transactions, 180 items</caption>
<thead><tr><th>Date</th><th>Description</th><th>Reference</th><th class="num">Debit</th><th class="num">Credit</th><th class="num">Balance</th></tr></thead>
<tbody>
%s
</tbody>
</table>
<p class="legal">%s</p>
</main>
</body>
</html>
""" % (money(r.randint(100000, 900000) / 100), money(r.randint(100000, 900000) / 100), money(bal), "\n".join(rows), paragraph(r, 3, 4))
    css = """body { margin: 0; font: 13px/1.4 "Helvetica Neue", Helvetica, Arial, sans-serif; color: #222; background: #fff; }
main { max-width: 900px; margin: 0 auto; padding: 32px 24px; }
h1 { margin: 0; font-size: 26px; color: #0b5d3b; }
.sub { margin: 4px 0 20px; color: #555; }
.summary { display: flex; flex-wrap: wrap; gap: 12px; margin-bottom: 20px; }
.card { flex: 1 1 180px; border: 1px solid #cfd8d3; border-left: 4px solid #0b5d3b; padding: 10px 14px; border-radius: 4px; }
.card span { display: block; font-size: 11px; text-transform: uppercase; color: #667; }
.card strong { font-size: 18px; }
.addr { line-height: 1.5; }
h2 { font-size: 18px; border-bottom: 1px solid #ccc; padding-bottom: 4px; }
table { width: 100%; border-collapse: collapse; font-size: 12px; }
caption { text-align: left; font-style: italic; color: #666; padding: 4px 0 8px; caption-side: top; }
th { text-align: left; background: #e8f1ec; padding: 6px 8px; border-bottom: 2px solid #0b5d3b; }
td { padding: 4px 8px; border-bottom: 1px solid #e3e6e4; }
.num { text-align: right; font-variant-numeric: tabular-nums; }
.ref { font-family: "Courier New", Courier, monospace; color: #555; }
tr.cr td:nth-child(5) { color: #0b5d3b; font-weight: bold; }
tr.dr td:nth-child(4) { color: #9b1c1c; }
.legal { margin-top: 24px; font-size: 11px; color: #777; text-align: justify; }
"""
    write("statement", html, css)


# ---------------------------------------------------------------- receipt

def receipt():
    r = rnd("receipt")
    items = []
    total = 0.0
    for _ in range(11):
        q = r.randint(1, 4)
        p = r.randint(199, 4999) / 100
        total += q * p
        items.append('<li><span class="n">%s</span><span class="q">%d &times; %s</span><span class="a">%s</span></li>'
                     % (words(r, 2).title(), q, money(p), money(q * p)))
    html = head("Receipt #50291") + """<body>
<div class="receipt">
  <h1>CORNER &amp; CO.</h1>
  <p class="c">214 Market Street<br>Open daily 8&ndash;22<br>Tel (555) 010-2299</p>
  <hr>
  <p class="row"><span>Receipt</span><span>#50291</span></p>
  <p class="row"><span>Date</span><span>2026-10-26 14:07</span></p>
  <p class="row"><span>Cashier</span><span>Marisol</span></p>
  <hr>
  <ol>
  %s
  </ol>
  <hr>
  <p class="row"><span>Subtotal</span><span>%s</span></p>
  <p class="row"><span>Tax 6%%</span><span>%s</span></p>
  <p class="row total"><span>TOTAL</span><span>%s</span></p>
  <p class="row"><span>Card **** 4417</span><span>%s</span></p>
  <hr>
  <p class="c small">Thank you for shopping with us.<br>Returns within 30 days with receipt.</p>
  <p class="barcode" aria-hidden="true">||| |||| | ||| || |||| | ||</p>
</div>
</body>
</html>
""" % ("\n  ".join(items), money(total), money(total * 0.06), money(total * 1.06), money(total * 1.06))
    css = """body { margin: 0; padding: 24px 0; background: #ddd; font: 13px/1.35 "Courier New", Courier, monospace; color: #111; }
.receipt { width: 302px; margin: 0 auto; padding: 18px 16px; background: #fff; }
h1 { margin: 0 0 6px; text-align: center; font-size: 20px; letter-spacing: 0.1em; }
.c { text-align: center; margin: 4px 0; }
.small { font-size: 11px; }
hr { border: 0; border-top: 1px dashed #333; margin: 10px 0; }
ol { list-style: none; margin: 0; padding: 0; }
li { display: grid; grid-template-columns: 1fr auto auto; column-gap: 8px; padding: 2px 0; }
li .a { text-align: right; min-width: 56px; }
li .q { color: #444; }
.row { display: flex; justify-content: space-between; margin: 2px 0; }
.total { font-size: 16px; font-weight: bold; margin: 6px 0; }
.barcode { text-align: center; font-size: 22px; letter-spacing: 2px; margin: 12px 0 0; transform: scaleY(1.6); }
"""
    write("receipt", html, css)


# ---------------------------------------------------------------- certificate

def certificate():
    html = head("Certificate of Completion") + """<body>
<div class="frame">
 <div class="inner">
  <p class="org">Harbor Institute of Technology</p>
  <h1>Certificate of Completion</h1>
  <p class="lead">This is to certify that</p>
  <p class="name">Priya Raghunathan</p>
  <p class="lead">has successfully completed the professional programme</p>
  <p class="course">Applied Data Engineering</p>
  <p class="detail">comprising 120 contact hours across 12 modules, completed with <strong>distinction</strong> in the term ending October 2026.</p>
  <div class="sign">
    <div><div class="line"></div><p>Dr. Elena Voss<br><span>Programme Director</span></p></div>
    <div class="seal" aria-hidden="true">&#9733;<br>HIT</div>
    <div><div class="line"></div><p>Marcus Lindqvist<br><span>Registrar</span></p></div>
  </div>
  <p class="id">Certificate no. HIT-2026-004871 &middot; Issued October 26, 2026</p>
 </div>
</div>
</body>
</html>
"""
    css = """body { margin: 0; background: #f4efe3; font-family: Cambria, Georgia, "Times New Roman", serif; color: #2b2b2b; }
.frame { box-sizing: border-box; width: 1000px; min-height: 700px; margin: 24px auto; padding: 18px; border: 10px double #8a6d1d; background: #fffdf6; }
.inner { box-sizing: border-box; min-height: 640px; border: 2px solid #8a6d1d; padding: 40px 56px; text-align: center; }
.org { margin: 0; font-size: 18px; letter-spacing: 0.3em; text-transform: uppercase; color: #8a6d1d; }
h1 { margin: 18px 0 24px; font-size: 46px; font-weight: normal; font-variant: small-caps; letter-spacing: 0.05em; }
.lead { margin: 10px 0; font-style: italic; font-size: 18px; }
.name { margin: 6px 0 12px; font-size: 40px; font-family: "Times New Roman", serif; border-bottom: 1px solid #8a6d1d; display: inline-block; padding: 0 40px 4px; }
.course { margin: 8px 0; font-size: 28px; font-weight: bold; }
.detail { max-width: 640px; margin: 12px auto 36px; font-size: 16px; line-height: 1.6; }
.sign { display: flex; justify-content: space-between; align-items: flex-end; margin: 0 20px 28px; }
.sign > div { width: 240px; }
.line { border-top: 1px solid #333; margin-bottom: 6px; }
.sign p { margin: 0; font-size: 15px; }
.sign span { font-size: 12px; color: #666; }
.seal { width: 96px; height: 96px; border-radius: 50%; border: 4px solid #8a6d1d; color: #8a6d1d; font: bold 16px/1.2 Arial, sans-serif; padding-top: 22px; box-sizing: border-box; }
.id { font-size: 12px; color: #777; margin: 0; }
"""
    write("certificate", html, css)


# ---------------------------------------------------------------- email (table layout, inline styles, as mail clients need)

def email():
    r = rnd("email")
    cells = "".join(
        '<tr><td style="padding:12px 0;border-bottom:1px solid #e5e7eb;font-size:15px;color:#374151;">%s</td>'
        '<td align="right" style="padding:12px 0;border-bottom:1px solid #e5e7eb;font-size:15px;font-weight:bold;color:#111827;">$%s</td></tr>'
        % (words(r, 3).title(), money(r.randint(900, 19900) / 100)) for _ in range(5))
    html = """<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Your order has shipped</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
</head>
<body style="margin:0;padding:0;background:#f3f4f6;">
<table role="presentation" width="100%%" cellpadding="0" cellspacing="0" border="0" style="background:#f3f4f6;">
<tr><td align="center" style="padding:24px 12px;">
  <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="width:600px;max-width:100%%;background:#ffffff;border-radius:8px;font-family:Arial,Helvetica,sans-serif;">
    <tr><td style="padding:28px 32px;background:#1f4e79;border-radius:8px 8px 0 0;">
      <img src="%s" width="40" height="40" alt="" style="display:block;border:0;">
      <h1 style="margin:12px 0 0;font-size:24px;line-height:1.25;color:#ffffff;font-weight:normal;">Your order is on its way</h1>
    </td></tr>
    <tr><td style="padding:28px 32px 8px;font-size:16px;line-height:1.55;color:#374151;">
      <p style="margin:0 0 14px;">Hi Sam,</p>
      <p style="margin:0 0 14px;">%s</p>
    </td></tr>
    <tr><td style="padding:0 32px;">
      <table role="presentation" width="100%%" cellpadding="0" cellspacing="0" border="0">
        %s
        <tr><td style="padding:14px 0;font-size:16px;font-weight:bold;">Total</td><td align="right" style="padding:14px 0;font-size:18px;font-weight:bold;color:#1f4e79;">$%s</td></tr>
      </table>
    </td></tr>
    <tr><td align="center" style="padding:20px 32px 28px;">
      <a href="https://example.test/track" style="display:inline-block;padding:12px 28px;background:#e07a1f;color:#ffffff;text-decoration:none;border-radius:6px;font-size:16px;font-weight:bold;">Track your package</a>
    </td></tr>
    <tr><td style="padding:18px 32px;background:#f9fafb;border-radius:0 0 8px 8px;font-size:12px;line-height:1.5;color:#6b7280;">
      <p style="margin:0 0 6px;">Order 77-1043 &middot; Estimated delivery Thursday, October 29</p>
      <p style="margin:0;">You are receiving this because you placed an order. <a href="https://example.test/prefs" style="color:#1f4e79;">Manage preferences</a></p>
    </td></tr>
  </table>
</td></tr>
</table>
</body>
</html>
""" % (LOGO, paragraph(r, 2, 3), cells, money(r.randint(5000, 30000) / 100))
    write("email", html, None)


# ---------------------------------------------------------------- report

def report(chapter_count=18, name="report", root=HERE):
    # The defaults write the committed template; the document benchmark asks for a longer one.
    r = rnd("report")
    chapters = []
    toc = []
    for c in range(1, chapter_count + 1):
        title = words(r, r.randint(2, 4)).title()
        cid = "ch%d" % c
        toc.append('<li><a href="#%s">%s</a></li>' % (cid, title))
        parts = ['<section id="%s">\n<h2><span class="no">%d.</span> %s</h2>' % (cid, c, title)]
        parts.append("<p class=\"lead\">%s</p>" % paragraph(r, 2, 3))
        for s in range(1, r.randint(3, 5)):
            parts.append("<h3>%d.%d %s</h3>" % (c, s, words(r, r.randint(2, 4)).title()))
            for _ in range(r.randint(2, 4)):
                parts.append("<p>%s <em>%s</em> %s <strong>%s</strong>.</p>" % (paragraph(r, 2, 4), words(r, 2), sentence(r), words(r, 2)))
            kind = r.randint(0, 4)
            if kind == 0:
                parts.append("<ul>" + "".join("<li>%s</li>" % sentence(r, 5, 12) for _ in range(r.randint(3, 6))) + "</ul>")
            elif kind == 1:
                parts.append("<ol>" + "".join("<li>%s<ul><li>%s</li></ul></li>" % (sentence(r, 5, 10), sentence(r, 4, 8)) for _ in range(r.randint(2, 4))) + "</ol>")
            elif kind == 2:
                body = "".join("<tr><th scope=\"row\">%s</th><td>%d</td><td>%s</td><td>%.1f%%</td></tr>" % (words(r, 2).title(), r.randint(10, 9999), money(r.randint(1000, 900000) / 100), r.random() * 100) for _ in range(r.randint(4, 9)))
                parts.append("<table><caption>Table %d.%d &mdash; %s</caption><thead><tr><th>Item</th><th>Count</th><th>Value</th><th>Share</th></tr></thead><tbody>%s</tbody></table>" % (c, s, words(r, 3), body))
            elif kind == 3:
                parts.append("<blockquote><p>%s</p><footer>&mdash; %s</footer></blockquote>" % (paragraph(r, 1, 2), words(r, 2).title()))
            else:
                parts.append("<pre><code>%s</code></pre>" % "\n".join("%s = %d;" % (r.choice(WORDS), r.randint(0, 999)) for _ in range(r.randint(3, 7))))
        parts.append("</section>")
        chapters.append("\n".join(parts))
    html = head("Annual operations report 2026") + """<body>
<header class="cover">
  <p class="kicker">Harbor Logistics Group</p>
  <h1>Annual Operations Report 2026</h1>
  <p class="byline">Prepared by the Office of Operations &middot; October 2026</p>
</header>
<nav class="toc" aria-label="Contents">
<h2>Contents</h2>
<ol>
%s
</ol>
</nav>
<main>
%s
</main>
<footer class="colophon"><p>Harbor Logistics Group &middot; Confidential &middot; %s</p></footer>
</body>
</html>
""" % ("\n".join(toc), "\n".join(chapters), words(r, 3))
    css = """body { margin: 0 auto; max-width: 760px; padding: 32px 24px 64px; font: 16px/1.6 Calibri, Carlito, "Segoe UI", sans-serif; color: #1b1b1b; }
h1, h2, h3 { font-family: Cambria, Caladea, Georgia, serif; line-height: 1.2; }
.cover { padding: 96px 0 72px; border-bottom: 4px solid #7a1f2b; margin-bottom: 40px; }
.kicker { text-transform: uppercase; letter-spacing: 0.2em; color: #7a1f2b; font-weight: bold; margin: 0 0 16px; }
.cover h1 { font-size: 48px; margin: 0 0 20px; }
.byline { color: #555; margin: 0; }
.toc ol { columns: 2; column-gap: 32px; padding-left: 1.4em; }
.toc li { margin: 0 0 4px; }
h2 { font-size: 28px; margin: 48px 0 12px; padding-bottom: 6px; border-bottom: 1px solid #bbb; }
h2 .no { color: #7a1f2b; margin-right: 6px; }
h3 { font-size: 20px; margin: 28px 0 8px; }
p { margin: 0 0 12px; text-align: justify; hyphens: manual; orphans: 2; widows: 2; }
.lead { font-size: 18px; color: #333; }
a { color: #7a1f2b; }
ul, ol { margin: 0 0 12px; padding-left: 1.6em; }
blockquote { margin: 16px 0; padding: 4px 18px; border-left: 4px solid #d9b8bd; background: #faf3f4; font-style: italic; }
blockquote footer { font-style: normal; font-size: 14px; color: #666; }
pre { background: #f4f4f4; padding: 10px 14px; overflow: auto; font-size: 14px; line-height: 1.4; }
code { font-family: "Courier New", Courier, monospace; }
table { border-collapse: collapse; width: 100%; margin: 16px 0 20px; font-size: 15px; }
caption { text-align: left; font-size: 14px; color: #555; padding-bottom: 6px; }
th, td { border: 1px solid #ccc; padding: 5px 9px; text-align: left; }
thead th { background: #f0e4e6; }
td:nth-child(n+2) { text-align: right; }
.colophon { margin-top: 64px; padding-top: 12px; border-top: 1px solid #bbb; font-size: 13px; color: #666; }
@page { size: A4; margin: 20mm; }
h2, h3 { break-after: avoid; }
table, blockquote, pre { break-inside: avoid; }
"""
    write(name, html, css, root)


# ---------------------------------------------------------------- paged (running headers, contents with leaders, repeated table header)

def paged():
    r = rnd("paged")
    titles = [words(r, 3).title() for _ in range(5)]
    toc = "\n".join('<li><a href="#s%d">%s</a></li>' % (i + 1, t) for i, t in enumerate(titles))
    body = []
    for i, t in enumerate(titles):
        body.append('<section>\n<h2 id="s%d">%s</h2>' % (i + 1, t))
        for _ in range(4):
            body.append("<p>%s</p>" % paragraph(r))
        if i == 2:
            rows = []
            for n in range(1, 91):
                rows.append('<tr><td>%d</td><td>%s</td><td class="num">%s</td></tr>' % (n, words(r, 3), money(r.randint(100, 900000) / 100)))
            body.append('<table>\n<thead><tr><th>No</th><th>Description</th><th>Amount</th></tr></thead>\n<tbody>\n%s\n</tbody>\n</table>' % "\n".join(rows))
        body.append("</section>")
    html = head("Paged sample report") + """<body>
<header class="cover">
<h1>Paged sample report</h1>
<p class="by">Running headers, a contents list with leaders and a table whose header repeats.</p>
</header>
<nav class="toc">
<h2 class="plain">Contents</h2>
<ol>
%s
</ol>
</nav>
%s
</body>
</html>
""" % (toc, "\n".join(body))
    css = """html { font: 12pt/1.4 "Liberation Serif", serif; color: #111; }
body { margin: 0; }
.cover { padding: 60px 0 40px; border-bottom: 3px solid #234; }
.cover h1 { font-size: 30pt; margin: 0 0 8px; }
.by { margin: 0; color: #555; }
h2 { font-size: 18pt; margin: 0 0 10px; string-set: chapter content(text); break-after: avoid; }
h2.plain { string-set: none; margin-top: 24px; }
section { break-before: page; }
p { margin: 0 0 10px; text-align: justify; orphans: 2; widows: 2; }
.toc ol { list-style: none; padding: 0; margin: 0 0 24px; }
.toc li { margin: 0 0 4px; }
.toc a { color: inherit; text-decoration: none; }
.toc a::after { content: leader(".") " " target-counter(attr(href url), page); }
table { border-collapse: collapse; width: 100%; margin: 12px 0; font-size: 11pt; }
th, td { border: 1px solid #999; padding: 3px 8px; text-align: left; }
thead th { background: #e8edf3; }
td.num { text-align: right; }
tr { break-inside: avoid; }
@page { size: A4; margin: 25mm 20mm; @top-center { content: string(chapter, first); font-size: 9pt; color: #555; } @bottom-center { content: "Page " counter(page) " of " counter(pages); font-size: 9pt; color: #555; } }
@page :first { @top-center { content: none; } }
"""
    write("paged", html, css)


if __name__ == "__main__":
    invoice()
    invoice_spanned()
    statement()
    receipt()
    certificate()
    email()
    report()
    paged()
