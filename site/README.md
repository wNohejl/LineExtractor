# site/

The XLines landing page: one static file, `index.html`, which says what XLines is, how to start
it, what it costs and whose data it holds (ADR 0031, Phase C). Its CSS is inline, it uses the
system font stack, and it fetches nothing from any other origin. The picture of the desk on it is
drawn in HTML and CSS with invented teams and values; it is not a screenshot and holds no data.

**Preview it** by opening `index.html` in a browser. No server and no build are needed.

**The repository address** is written once, in `<meta name="xlines-repository">` in the page's
`<head>`. A short inline script fills the clone command and every repository link from it, so
when the project moves (ADR 0031, Phase B) that one line is the only change.

**Publishing** is `.github/workflows/pages.yml`, which uploads this folder as it is to GitHub
Pages. It runs only by hand (Actions → Pages → Run workflow), and nothing is published until the
owner has set Settings → Pages → Source to "GitHub Actions" and runs it. Everything in this
folder is published, this file included.
