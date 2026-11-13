"""Generate PNG icons at all sizes + multi-size .ico from the cleaned SVG."""
import os, sys
from pathlib import Path

# Read the icon-photo.html to extract the SVG symbol
html_path = Path(__file__).parent / "icon-photo.html"
html_content = html_path.read_text(encoding="utf-8")

# We need a self-contained HTML page that renders just the icon at a given size
# Extract everything between the <svg ...defs...> block and reuse it
def make_render_page(size):
    """Create an HTML page that renders the icon at exactly `size` px."""
    return f"""<!DOCTYPE html>
<html><head><meta charset="UTF-8">
<style>
  *, *::before, *::after {{ box-sizing: border-box; margin: 0; padding: 0; }}
  body {{ background: transparent; margin: 0; padding: 0; width: {size}px; height: {size}px; overflow: hidden; }}
</style>
</head><body>
{html_content.split('<body>')[1].split('<div class="grid">')[0]}
<svg id="icon" width="{size}" height="{size}"><use href="#axolotl-photo"/></svg>
</body></html>"""

sizes = [16, 24, 32, 48, 64, 128, 256, 512, 1024]
ico_sizes = [16, 24, 32, 48, 64, 128, 256]
out_dir = Path(__file__).parent / "icons"
out_dir.mkdir(exist_ok=True)

from playwright.sync_api import sync_playwright
from PIL import Image

with sync_playwright() as p:
    browser = p.chromium.launch()

    for size in sizes:
        page = browser.new_page(viewport={"width": size, "height": size})
        page.set_content(make_render_page(size))
        page.wait_for_timeout(300)

        icon_el = page.locator("#icon")
        out_path = str(out_dir / f"icon-{size}.png")
        icon_el.screenshot(path=out_path, omit_background=True)

        fsize = os.path.getsize(out_path)
        print(f"  icon-{size}.png  ({fsize:,} bytes)")
        page.close()

    browser.close()

# Build .ico
print("\nBuilding app.ico...")
ico_images = []
for s in ico_sizes:
    img = Image.open(str(out_dir / f"icon-{s}.png"))
    ico_images.append(img)

ico_path = str(out_dir / "app.ico")
ico_images[0].save(ico_path, format="ICO", sizes=[(s, s) for s in ico_sizes], append_images=ico_images[1:])
print(f"  app.ico  ({os.path.getsize(ico_path):,} bytes)")

# Copy to app resources
import shutil
app_ico = Path(__file__).parent / "../../src/Ascolotl.App/Resources/app.ico"
app_png = Path(__file__).parent / "../../src/Ascolotl.App/Assets/app-icon.png"
shutil.copy2(ico_path, str(app_ico))
shutil.copy2(str(out_dir / "icon-256.png"), str(app_png))
print(f"\nDeployed to:")
print(f"  {app_ico}")
print(f"  {app_png}")
print("\nDone!")
