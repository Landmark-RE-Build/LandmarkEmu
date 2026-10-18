# wiki/

This folder is the source of the project's **GitHub wiki**. This README is not a page: don't copy it into the wiki
(there it would show up as a page called "README").

- One file per page. The file name is the page title with `-` for spaces: `Client-Connection-Guide.md` is the page
  "Client Connection Guide"; `Home.md` is the start page.
- Links between pages are written `[text](Page-Name)`, without `.md`, which is how GitHub wikis link pages.
- `_Sidebar.md` is the navigation shown on every page; `_Footer.md` the line at the bottom.
- The pages follow the same rules as the rest of the public repository: no local paths, personal data, game files,
  decompiled code or copied third-party text.

Edit the pages here, in the main repository, so changes are reviewed together with the code they describe.

## Publishing (maintainers)

A GitHub wiki is a separate git repository next to the main one, `LandMarkEmu.wiki.git`. It exists once the wiki has
been enabled in the repository's settings and a first page has been saved on GitHub (any text; it is replaced below).

```
git clone https://github.com/XDigitalPixelX/LandMarkEmu.wiki.git
cd LandMarkEmu.wiki
git rm -q *.md                                   # start from an empty page set (so removed pages go too)
cp ../LandMarkEmu/wiki/*.md .                   # the pages from this folder
rm README.md                                     # not a page
git add -A
git commit -m "Update the wiki from wiki/"
git push
```
