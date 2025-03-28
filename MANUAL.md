# Alpha Manual

This is a very loose guide for all of the hard-to-find features in Alpha. You're encouraged to click around and break things. If you do break things (which is likely because I write bad code), feel free to leave an issue on this repository.

## Understanding windows

Alpha is a window (on your desktop) of windows (in ImGui), and you can have multiple windows open inside of Alpha. Each window has a game install attached to it, and you can switch between the game installs you add to Alpha.

There's an Excel window and a filesystem window. You can open as many of these as you want, but each window can load quite a bit of data, so be careful if you don't have a lot of RAM. (This is entirely my fault for not being efficient with resources but I haven't had the time/motivation to fix it.)

Most windows will have a `#` button that you can click for extra actions, like switching game installs. There's also usually a separator you can click and drag.

## Excel view

The left sidebar lets you pick between sheets. There's a search box at the top to filter by sheet name. You can right click the search box to change it to full text search mode, which allows you to type a string and show the sheets that contain that string.

When selecting a sheet, the search box above the sheet will filter the rows in that sheet. You can use the `$` character to write a C# query (e.g. `$Row.Name.ToString().Contains("Sprint")` in the Action sheet).

Right click cells to copy their content. Cells that have certain properties in the schema (like images or links to other sheets) will be replaced with special elements you can interact with.

Note that mapping Excel data is a community effort, and Alpha only downloads the latest schemas from EXDSchema/SaintCoinach. Sheets might look weird on old game versions.

## Filesystem view

After opening the filesystem view, it'll take a second to calculate all of the files, and then you can browse it normally. Files/folders beginning with `~` have unknown names, but you can still access and extract them.
