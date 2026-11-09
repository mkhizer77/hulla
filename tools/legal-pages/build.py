"""Builds the privacy and terms pages of the link site from docs/legal (docs/legal/README.md).

Run from the repository root after editing docs/legal/privacy.md or terms.md:
    python3 -m pip install markdown   # once
    python3 tools/legal-pages/build.py
    pnpm exec prettier --write infra/cloudflare/links/privacy infra/cloudflare/links/terms
The pages land in infra/cloudflare/links/privacy/ and terms/; a push to main deploys them.
"""

import html
import pathlib
import re

import markdown

ROOT = pathlib.Path(__file__).resolve().parents[2]
SITE = ROOT / "infra" / "cloudflare" / "links"

PAGES = {
    "privacy": ("docs/legal/privacy.md", "Privacy policy"),
    "terms": ("docs/legal/terms.md", "Terms of service"),
}

TEMPLATE = """<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
    <title>{title} · Hulla</title>
    <meta name="description" content="{title} for Hulla: desi games with friends." />
    <meta name="theme-color" content="#7cc8f2" />
    <link rel="stylesheet" href="/site.css" />
  </head>
  <body class="legal">
    <main>
      <a href="/"><img class="logo" src="/assets/logo.webp" width="560" height="435" alt="Hulla" /></a>
      <article class="card">
{body}
      </article>
      <footer>
        <a href="/privacy/">Privacy</a> · <a href="/terms/">Terms</a> · <a href="/delete/">Delete your account</a> ·
        <a href="/support/">Support</a>
      </footer>
    </main>
  </body>
</html>
"""


def build(source: str, title: str) -> str:
    text = (ROOT / source).read_text(encoding="utf-8")
    # The quoted note at the top is for maintainers, not players.
    text = re.sub(r"^> .*\n\n", "", text, count=1, flags=re.M)
    body = markdown.markdown(text, extensions=["tables"], output_format="html")
    body = "\n".join("        " + line if line else "" for line in body.splitlines())
    return TEMPLATE.format(title=html.escape(title), body=body)


def main() -> None:
    for slug, (source, title) in PAGES.items():
        out = SITE / slug / "index.html"
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(build(source, title), encoding="utf-8")
        print(f"wrote {out.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
