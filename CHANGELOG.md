# PiP Studio changelog

Newest first. Each `## x.y.z` section becomes the notes of that GitHub release.

## 1.0.1

Getting ready for the Microsoft Store. If you use the installer or the portable version, nothing changes for you.

- When it runs as the Store version, "Start when I sign in" uses Windows' startup apps list, and "Settings folder" opens the right place
- The "Restart as administrator" option is hidden in the Store version, since it can't do that

## 1.0.0

First release! 🎉

PiP Studio lets you pop any window out into a small floating picture-in-picture, so you can keep a video, a stream or a guide on screen while you play or work.

- Works with any app, a whole screen, or just part of one
- Also controls the PiPs from your browser, Discord and other apps
- Snaps neatly into corners and stays out of your way
- Can be used entirely with a controller
- Light and dark themes that match Windows

Full disclosure: this app is vibecoded. I built it together with AI (Claude), so expect the odd rough edge, and please report anything weird you run into.

Download **PiPStudio-Setup-1.0.0.exe** below to install it, or grab the portable zip if you don't want to install anything.

If Windows shows a "Windows protected your PC" warning, click **More info** and then **Run anyway**.

Found a bug or have an idea? [Let me know](https://github.com/NauticalNoodles/PiP-Studio/issues/new/choose)!

---

# Development history

Builds made before the first public release, kept for reference.

### Build 4.21.1

- Ready for GitHub: MIT license, third-party notices, an installer (per user, no admin, includes .NET, optional start at sign-in, uninstaller that can remove settings), a portable zip, and a release workflow that builds both for every GitHub release.
- The exe now has the PiP Studio icon.
- `build.bat release` builds the release files locally.

### Build 4.21.0

- **Options for each PiP** on the Running PiPs page: the new ⋯ button on a row, a right-click on the row, or Options in the command bar opens a card with that PiP's own settings, for PiP Studio's PiPs and other apps' PiPs alike:
  - Opacity and volume sliders.
  - Size (the 8 presets).
  - Shape: match the picture, 16:9, 4:3, 21:9, 1:1 or 9:16.
  - Black bars: PiP Studio's PiPs crop away the bars around the video (checked again when the source changes); other apps' PiPs fit the window to the video, like Discord pop-outs already did.
  - Snapping: follow your setting, always snap to spots, or never snap.
- PiP Studio's PiPs keep these with the rest of their state; for other apps' PiPs they last until that window closes.

### Build 4.20.0

- **Other apps' PiPs are found automatically**, not just browser PiPs and Discord pop-outs: small always-on-top windows of other apps count too (video players' mini modes, Chrome document PiPs like Spotify, YouTube Music and Meet, Telegram and Zoom floating videos...). They show on Running PiPs and get the same controls, snapping, sizes and keep-on-top. Settings > Behavior > Find other apps' PiPs automatically (on by default).
- **Not a PiP** (Running PiPs page) for anything it picked up by mistake, and **Treat as a PiP** (Windows page) for one it missed. For browsers and Discord the mark lasts until that window closes; for other apps it's remembered. Settings > Behavior > Windows you marked > Reset forgets them.

### Build 4.19.2

- The Running PiPs page folds the side menu at the same width as the Windows and settings pages (about 1,300 px with explanations on, about 1,010 px with them off), so the menu no longer pops open when you switch to it.
- Fixed a build error in 4.19.0 (title bar settings).

### Build 4.19.0

- **Previous / Next window buttons** on the PiP title bar.
- **Customizable title bar:** Settings > Appearance > PiP window > Title bar buttons. Switch each button on or off and move it left or right: Previous window, Next window, Change source, Theater mode, Crop, Next corner, Next display, Bring source to front, Click-through, More options (always shown) and Close. With an animated explanation that shows your title bar, and a reset.
- On a narrow PiP the leftmost buttons make room first; Close and More options are the last to go.

### Build 4.18.0

- Tidy-up: removed dead code (unused methods, native imports, the Discord bar test logging and the old translation hooks), shared the list and title styles between the main window and the window picker, project version set to 4.18.0, README rewritten with this changelog split out.

### Build 4.17.0

- **New window picker** (Ctrl+Alt+P and "Change source…"), in the same style as the rest of PiP Studio: page title and commands (Click a window, Screen region, Refresh), a search box, the list of windows and displays with icons, app and status, and a details card with a live preview of the selected window and an Open button.
- More reliable: one live preview instead of one per window, no window grid to lay out, rows for windows that closed are refreshed instead of failing, and the window always shows even if its first frame is late.
- Keys: type to search, Up/Down/Page Up/Page Down to move, Enter or double-click to open, Esc clears the search and then closes, F5 refreshes.

### Build 4.16.5

- Language test: new and changed text is translated before it's drawn, so English no longer flashes during animations or when the explanation changes on hover.

### Build 4.16.4

- Language test (Arabic): the settings window now sets right-to-left reading on each piece of Arabic text itself (WPF ignored the invisible direction marks), with its alignment swapped so it stays in the same place. Drop-down lists too. Text drawn on PiPs, menus and notifications keep using the direction marks.

### Build 4.16.3

- Language test (Arabic): sentences now read right to left, so English words inside them ("Windows", "PiP Studio", key names) sit on the correct side, and punctuation lands at the end of the sentence. The layout still doesn't move.

### Build 4.16.2

- Language test: text that changes or appears later (page switches, status changes, dialogs, the explanation panel, drop-down lists, tooltips) is now translated too, not just what was on screen when the window first opened.

### Build 4.16.1

- Other apps' PiPs come to the front as soon as you steer them (controller controls or shortcuts), and stay always on top afterwards, so Discord's pop-out no longer disappears behind the game when controller controls end. If the app or a game drops it behind, PiP Studio puts it back on top, until that PiP closes.

### Build 4.16.0

- New smallest size: **Extra small** (8 sizes now). Your default size stays the same.
- Other apps' PiPs no longer slide off the screen when you make them smaller than the app allows (Discord has a minimum size): PiP Studio re-anchors the window with the size the app really took, and Smaller stops at that minimum ("Smallest size").

### Build 4.15.6

- Discord black-bar trimming confirmed working; the test logging to %AppData%\PiPStudio\bars is off again (the folder can be deleted).

### Build 4.15.5

- Discord bars: the measurement now reads past Discord's thin dark-grey window frame into the black bar beside the video (it used to stop at the frame, so the bars looked like 0 px).

### Build 4.15.4

- Discord bars: a first check that finds no bars no longer locks in the shape (only a shape reached by trimming is remembered), so later size steps keep checking.
- Testing aid: each check saves what it captured and measured to %AppData%\PiPStudio\bars (images plus log.txt, last 12 kept).

### Build 4.15.3

- Discord bar detection reads Discord's own picture of the window instead of the screen. The pop-out is see-through beside the video, so the screen showed whatever was behind it (not the bars). The screen copy is only a fallback when Discord can't draw its picture.

### Build 4.15.2

- Discord bar detection fixed: Discord's bars are dark grey, not black, so bars are now any solid dark color. They're measured on the middle of each side (the title strip's buttons and rounded corners no longer get in the way). The title strip on top and any even margin around the video aren't counted as bars.

### Build 4.15.1

- Discord pop-outs: black bars on the left and right make the window taller, bars on the top and bottom make it shorter (the width stays on the size preset). Once the picture has no bars, that shape is remembered for as long as the window stays open, so later size steps use it straight away without checking again.

### Build 4.15.0

- **7 size presets:** Small, Small+, Medium, Medium+, Large, Large+ and Extra large, set by width so a PiP keeps its shape. Bigger / Smaller (shortcuts, controller, menu, mouse wheel) step through them; holding steps at a steady pace. The PiP menu and the default size offer all seven. Your old default size carries over.
- **Other apps' PiPs use the same presets.** Browser PiPs keep their shape.
- **Discord pop-outs** start at 16:9. After each size step PiP Studio looks for black bars around the video and trims the window until they're gone (a few quick passes).

### Build 4.14.1 (language test)

- Picking a language now switches straight away: the window rebuilds in place on the same page, no restart.
- Arabic no longer mirrors anything: the layout stays exactly as in English, only the text changes.

### Build 4.14.0 (language test)

- **Languages (test):** Settings → General → Language: Use my Windows language, English, العربية (Arabic) or 中文（简体） (Chinese Simplified). PiP Studio restarts to switch.
- Arabic switches the settings window and the window picker to right-to-left. The explanation animations keep real screen sides.
- Text drawn on PiPs, the tray and PiP menus, and tray notifications are translated too.
- Settings search also matches the translated names.
- Translations live in `strings.tsv` (English, Arabic, Chinese, separated by tabs); `LangData.cs` is generated from it.

### Build 4.13.11

- The pointer options (what happens when the pointer is over a PiP, move arrows, fade and click-through opacity) moved from Appearance to Behavior > Mouse. Lite mode and animation effects now sit under Appearance > Effects.

### Build 4.13.10

- The "Move out of the way" explanation now shows the PiP sliding from right to left, as it really does, instead of from bottom to top.

### Build 4.13.9

- Other apps' PiPs are snapped once when they appear and again only after you drag them. If the app moves its window by itself (Discord overlays, for example), PiP Studio stops snapping that window instead of fighting the app over it.

### Build 4.13.8

- With explanations turned on, the Windows page now folds the side menu at the same width as the settings pages (about 1,300 px), so it no longer pops open when you switch to it.

### Build 4.13.7

- The side menu now folds at the same width (about 1010 px) on the Windows page and on every page without explanations showing.

### Build 4.13.4

- **Menu width follows the page.** On settings pages with explanations shown the side menu folds to icons until the window is about 1,300 px wide, so the settings aren't squeezed. With explanations hidden (and on pages without a right-hand panel) it stays open down to about 860 px; on the Windows page it stays open as long as the preview fits beside it (about 1,000 px).

### Build 4.13.2

- **Slimmer side menu.** The open menu is 200 px wide (was 290), giving the settings more room.

### Build 4.13.1

- **New PiPs can open in the middle of an edge.** With 8 spots on (the default), Behavior > Where and how they open > Position also offers Top middle, Bottom middle, Left middle and Right middle. Turning 8 spots off hides them again (a middle choice then falls back to a nearby corner).

### Build 4.13

- **Shorter setting descriptions.** The text next to each setting is now a short line; the full explanation moved under the animation in the explanations panel (also shown on the Settings page now, and as a tooltip on the short line).

### Build 4.12.1

- **Explainers stay on the right setting.** The panel now goes by the setting actually under the pointer (checked again after scrolling settles), ignores the pointer while a drop-down is open, and after you pick an option it shows the setting you just changed, so it no longer jumps to whatever ended up under the closed drop-down or scrolled past the pointer.

### Build 4.12

- **Hide the explanations.** The explanations panel has a Hide button, and the Appearance, Behavior and Controller pages have a Hide / Show explanations button at the top, so the settings can use the full width. On a controller, X (Square on PlayStation, Y on Switch) toggles it, shown in the hint bar. The choice is remembered.
- Labels on the animations now sit on a grey box.

### Build 4.11.6

- **Explainer PiPs in your accent color.** The PiPs in the setting animations are a solid block of your current accent color.
- **Animations restart as one.** Instead of the pointer and the PiP each travelling back to the start separately, a scene now holds its final frame for a moment, fades out, and starts again from the beginning all at once.
- The settings scrollbar no longer overlaps the explainer panel.

### Build 4.11.4

- **Clearer explainers.** The two grey windows behind every animation are gone (scenes that need a window behind the PiP draw their own). Labels on the animations sit on a black box with a thin border so they're readable over anything. The Rounded corners scene shows your current choice, labelled, with a clearly visible rounding.

### Build 4.11.3

- **Explainers follow your choices.** Change a setting and its animation updates right away. Pointer over a PiP shows the action you picked (controls, fade to your level, nothing, or moving out of the way); Animation effects shows a glide or a jump; Snap shows dragging and letting go (to the nearest of your 8 or 4 spots, or staying put with snapping off); Size limits grows a tall and a wide app up to your height and width limits; More than one PiP shows a second PiP opening, or the one PiP switching; Right-click shows what your right-click does. The Size and Snap animations no longer run off the edge.
- **No animation, no box.** Settings without an animation now show just their name and description.

### Build 4.11.2

- **More accurate explainers.** App theme shows a mini PiP Studio window in light and dark (or just the one you picked); Accent color cycles that window through colors; Animation effects compares a gliding PiP with a jumping one; Lite mode shows the see-through, shadowed window turning plain; Show move arrows shows the pointer clicking an arrow and the PiP hopping to the next spot. Accent color no longer shows the border animation.

### Build 4.11.1

- **Explainers fixed.** Position and Size now have their own animations (a new PiP popping up in your chosen corner; the four sizes with yours highlighted). Settings without an animation show their own name and description over a quiet scene, instead of leaving the previous animation up.

### Build 4.11

- **Animated explainers for settings.** On the Appearance, Behavior and Controller pages, a panel on the right (like the Windows page preview) plays a short looping animation of the setting you point at or move the focus to: snap spots, edge gap, hover actions, ghost mode, rounded corners, border, messages under the PiP, follow, hiding while the source is in front, hiding over full-screen apps, size limits, opacity, theater mode, minimized sources, more than one PiP, other apps' PiPs and controller controls. Scenes use your current settings (spots, limits, opacity, border color). With Lite mode or animations off they show a still frame; the panel hides when the window is narrow.

### Build 4.10.1

- **GPU option removed.** "Use the high-performance GPU" made little difference, so it's gone. On first start, PiP Studio removes the graphics preferences it had set (yours are left alone).

### Build 4.10

- **Other apps' PiPs snap to spots** (Settings > Behavior, on by default). Chrome, Firefox, FreeTube and Discord PiP windows land on the same spots as PiP Studio's PiPs: when they open, and when you let go after dragging or resizing them. Uses your edge gap and 4/8-spot setting; theater mode is left alone. Moving them with your shortcuts or controller also uses your edge gap now.

### Build 4.9.5

- **No more video controls popping up.** Turning controller controls on or off no longer makes video players under the mouse (FreeTube's PiP, YouTube...) show their controls. PiP Studio used to send an invisible "empty" mouse event while switching focus, which those players read as the mouse moving; it now switches focus without any fake input.

### Build 4.9.4

- **Discord pop-outs count as app PiPs.** A call or stream popped out of Discord (also PTB, Canary, Vesktop and Legcord) is now treated like a browser's PiP: your shortcuts and controller steer it, it's listed on the Running PiPs page as "Discord's own PiP", gets the controller border and hints, and is part of the next/previous cycle. Show (A) brings up Discord's main window.

### Build 4.9.3

- **Show brings up the app.** Pressing A (Show) while steering another app's PiP now brings up that app's own window (the browser window playing the video), not just the PiP. Like on PiP Studio's own PiPs, turning controller controls off afterwards won't send you back to the game.

### Build 4.9.2

- **Clear message for unsupported controls.** Using ghost mode, hide, follow, dodge, crop or another PiP Studio-only control while steering another app's PiP now shows a note under that window, such as "Ghost mode isn't available for Chrome's own PiP", instead of a "No PiP is open" notification. Shortcuts that don't depend on a PiP (open PiP Studio, new PiP, play/pause...) work as usual.

### Build 4.9.1

- **Levels on other apps' PiPs.** Changing the volume or opacity of another app's PiP shows the level and a bar under it, just like PiP Studio's own PiPs. Resizing shows the new size, and mute shows Muted / Sound on.

### Build 4.9

- **Next / previous reach other apps' PiPs.** The next and previous window shortcuts (and LB / RB) now cycle through Auto, every window, and then other apps' own PiP windows. Landing on one closes PiP Studio's PiP and hands your controls to that window; going on from there opens a PiP Studio PiP again (with controller controls still on).
- **"Chrome's own PiP".** When your controls move to another app's PiP, a note under it says whose PiP it is, and its name pill (with controller controls on) and its row on the Running PiPs page use the same wording. App PiP windows are no longer listed as ordinary windows on the Windows page.

### Build 4.8

- **Other apps' PiPs on the Running PiPs page.** Chrome, Firefox, FreeTube (and other apps') own picture-in-picture windows are listed with their position, size and opacity, marked "App's own PiP". Selecting one gives Next corner, Bigger, Smaller, Mute and Close.
- **Controller border and hints on them too.** While controller controls are on and you're steering another app's PiP, it gets the same accent border (white flash on each press), its name at the top and the button hints at the bottom. The name and hints fade after a few idle seconds like on PiP Studio's own PiPs; the border stays.

### Build 4.7

- **Control other apps' PiP windows** (Settings > Behavior, on by default). When no PiP Studio PiP is open, your shortcuts and controller steer the picture-in-picture window of Chrome, Edge, Brave, Opera, Firefox and Electron apps such as FreeTube: move and snap to the 8 spots, next corner, next monitor, grow/shrink (respecting your size limits), theater, opacity, volume, mute and close. Start + Select toggles controller controls for it too (instead of opening a new PiP).

### Build 4.6.2

- **Lighter over games.**
  - PiP Studio runs at below-normal CPU priority, so it never competes with a game (the PiP picture itself is drawn by Windows).
  - The live preview on the Windows page is only drawn while PiP Studio's window is in use, instead of being a second copy Windows keeps drawing behind your game.
  - With controller controls on, the PiP's name and button hints fade after 4 seconds without input; any button brings them back. Can be turned off in Settings > Controller.

### Build 4.6.1

- **Big performance fix for games.** Since 4.3, PiP Studio checked all 4 XInput slots and 8 joystick slots 60 times a second, including empty ones. Checking an empty slot is slow, and doing it every frame stalled the CPU enough to cost games a lot of frames. Connected controllers are still read every frame; empty slots are now only checked every 2 seconds (a newly plugged-in controller is picked up within 2 seconds).

### Build 4.6

- **Use the high-performance GPU** (Settings > Behavior, on by default). PiP Studio sets Windows' own per-app graphics preference to High performance for itself and for every app you put in a PiP, so on laptops with two GPUs they render on the dedicated one instead of the integrated one. Apps switch the next time they start (the PiP tells you once per app). Preferences you set yourself are never changed, and turning the option off removes only the ones PiP Studio added.

### Build 4.5.1

- **Works with Xbox mode.** Xbox mode shows one window at a time and hides the rest from the list of windows the way other virtual desktops are hidden, so PiP Studio saw nothing. Those hidden windows are now listed (and work with Auto and next/previous). Windows on other virtual desktops show up too.

### Build 4.5

- **Freeze warning on the row.** Chromium-based apps (Chrome, Edge, Discord, Spotify, FreeTube...) now have a small warning icon next to their name in the Windows list, at any window size. Clicking it opens a dialog with both fixes: **Restart with the fix** now, and **Always start it with the fix** (edits the app's shortcuts). With a controller, press X on the row (or right, when the window is narrow); in the dialog, up reaches the switch, A flips it, and the buttons work as usual.

### Build 4.4.2

- **Questions work with a controller.** The "Restart with the fix" confirmation and the administrator/mirroring messages are now shown inside PiP Studio instead of as Windows message boxes. Left/right picks a button, A answers, B cancels; Enter and Esc work on the keyboard. Risky questions start on Cancel.

### Build 4.4.1

- **Details panel reachable with a controller.** Right from the window list (or the search box) moves to the details panel's Open in picture-in-picture button; up/down reach the freeze fix under it, and left goes back to the list.
- **Notifications do something when clicked.** Clicking one of PiP Studio's notifications now opens the right place: the Shortcuts page for taken shortcuts, the Controller page when no controller is found, brings hidden PiPs back, opens errors.log for errors, and otherwise opens PiP Studio.

### Build 4.4

- **Size limits** (Settings > Behavior > Size limits). Set a maximum width, a maximum height, or both, as a share of the screen. They apply to new PiPs, the size presets, growing with the shortcut or controller, original size, and when a PiP switches to a window with a different shape. Theater mode ignores them. Height is limited to 50% of the screen by default so tall apps (phone mirrors, chat windows) don't take over; changes apply to open PiPs right away.

### Build 4.3.6

- **Switching to Auto shows your previous window.** Cycling into Auto with the next/previous shortcuts (or turning it on from the menu) now jumps straight to the window you were using before the current one, and the message under the PiP names it.

### Build 4.3.5

- **Auto never shows the window you're in.** When the window an Auto PiP is showing comes to the front (you switched to it, or used Show), the PiP moves to the window you just left, or the next available one, instead of hiding or duplicating what's already on screen.

### Build 4.3.4

- **No more Alt key presses.** Switching focus (controller controls on/off, Show) no longer simulates the Alt key, which some games picked up as keyboard input. Games that lock the foreground are now handled the way Alt+Tab switches windows, without sending any keys.

### Build 4.3.3

- **Show stays shown.** After pressing A (Show) to bring a PiP's window to the front, turning controller controls off no longer jumps you back to the window you were in before.
- **PiP controls come first.** While controller controls are on, the controller steers the PiP even when the PiP Studio window is in front. With them off, it drives the window as before, and the on/off and start combos always reach the PiP.

### Build 4.3.1

- **Details panel moved up.** On the Windows page the details panel (preview, title, Open button, the Chromium freeze fix) now starts level with the page title instead of below the buttons, so it has more room. If the window is still too short, the part under the preview scrolls.

### Build 4.3

- **Several controllers at once.** Every connected controller now works, not just the first one found: for example an Xbox controller and a DualShock together. Their buttons are combined, and the button names in hints follow whichever controller you used last. Xbox controllers are only read once (through XInput), so they don't double up.

### Build 4.2

- **Search with a controller.** In controller-friendly mode, Y opens search: the window search on the Windows page, the settings search on every other tab. An on-screen keyboard slides up from the bottom; the D-pad moves between keys, A types, X deletes, Y adds a space, and B or Menu closes it and jumps to the results.
- **Window search reachable.** Up from Auto now goes to the Search windows box (and up again to the page buttons); A on it opens the keyboard.

### Build 4.1

- **Start a PiP from the controller.** New binding "Start a PiP on Auto (when none is open)". By default it shares Start + Select (Menu + View / Options + Share) with controller controls: with no PiP open, the combo opens an Auto PiP and turns controller controls on so you can place it straight away; with a PiP open, it toggles controller controls as before. Keyboard: Ctrl+Alt+I.
- **Controller UI stays up when the PiP is hidden.** While controller controls are on, the PiP's name (a pill at the top), the button hints, messages, the volume bar and the outline stay visible even when the picture itself is hidden (hide all, hide while its window is in front, hide in full screen, or faded out). Only the picture is left out.

### Build 4.0

- **Separate searches.** The search box in the side menu now only finds settings. The Windows page has its own "Search windows" box above the list (Ctrl+F on that page). Esc clears it, Down or Enter jumps into the list. A search with no matches now empties the list instead of leaving the old rows.
- **Running PiPs fix.** A PiP you closed no longer stays listed on the Running PiPs page when you come back to it.
- Enter in the settings search opens the first result again.

### Build 3.9

- **No white flash.** The window (and the window picker) stays invisible until its first frame is drawn, so reopening from the tray no longer flashes white. Lite mode's solid background now uses Windows 11's own dark or light app color directly.

### Build 3.8

- **Hold to keep going.** On a controller, holding the button or stick for volume, opacity or size keeps changing it (after a short pause, about 12 steps a second) until you let go. The keyboard shortcuts already repeat while held.
- **8 snap spots by default.** PiPs snap to the corners and the middle of each edge. Applied to existing settings too.
- **Black border by default.** When the border is turned on it's black, unless you'd already picked a color. While controller controls are on, the border switches to your accent color so you can tell they're on.

### Build 3.7

- **Lite mode** (Settings > Appearance) for slower PCs. It turns off every animation (in the app and on PiPs, including smooth scrolling), the Mica and acrylic backgrounds, drop shadows, and live previews: the Windows page hides its preview, and the window picker shows app icons instead of live thumbnails. The PiPs themselves keep working exactly the same. It applies right away.

### Build 3.6

- **Volume bar.** Changing a PiP's volume (shortcut, controller or menu) shows a caption under the PiP with the level and an accent bar that fills as it goes up.
- **Outline while controller controls are on.** If the PiP is faded out (low opacity or the hover fade), a solid outline in your border color is drawn over it while controller controls are on, so you can always see where it is. It flashes white on each button press, like the normal border.

### Build 3.5

- **Release focus works straight from the game.** Many games lock the foreground so other apps can't take it, which is why releasing focus only worked after clicking another window first. PiP Studio now holds Alt for the moment it switches (Windows lifts the lock while Alt is down), both when taking focus from the game and when handing it back. The game never sees a full Alt press, so it won't open a menu or similar.

### Build 3.4

- **Volume shortcuts.** Turn the PiP'd app's volume up or down in 5% steps: Ctrl+Alt+Shift+Up / Down on the keyboard (hold to repeat), and R-Stick → / ← on a controller. Turning the volume up also unmutes. Both can be changed in Shortcuts and Controller.
- **Complete controller hints.** The hints on the PiP now list every bound control, including opacity, volume, mute, hide and ghost mode, most important first.
- **Paired hints.** Controls that come in pairs share one hint. When both halves sit on one axis of a stick or the D-pad, it reads like "R-Stick ↕  Opacity" or "R-Stick ↔  Volume"; otherwise "LB / RB  Source".

### Build 3.3

- **Page title above the commands.** The page title now has its own line, with the page's buttons (Open in PiP, Change source, Crop...) underneath. The title no longer gets cut off in a small window, and the buttons wrap onto a second line instead of squeezing it.

### Build 3.2

- **Page commands reachable with a controller.** Press up from the top of a page (or from Auto on the Windows page) to reach the buttons at the top: Open in PiP, Click a window, Screen region, Refresh. Left and right move between them, and down goes back into the page.
- **Keyboard switches controller mode off.** With "When I use a controller", the normal layout now comes back when you press a key, not when the mouse moves.

### Build 3.1

- **Tabs in controller mode.** In controller-friendly mode the side menu is replaced by tabs across the top of the window, with LB and RB chips on each side. The accent underline slides to the current tab as you switch. B goes back to the Windows tab. Tabs can also be clicked.

### Build 3.0

- **Controller-friendly mode.** Settings > Controller > Controller-friendly mode: Off, When I use a controller (default), or Always. While PiP Studio's window is focused, the controller drives the window instead of the PiP. The layout gets 15% bigger, an accent focus ring glides between items, and a bar at the bottom shows the buttons for your controller.
  - D-pad or left stick: move around. Holding a direction repeats.
  - A / ✕: select, open the chosen window in a PiP, flip switches, open drop-downs.
  - B / ○: back. Closes a drop-down or the menu, then goes to the menu, then to the Windows page.
  - LB / RB: previous / next page. Menu: open the navigation. Right stick: scroll.
  - The Guide / PS / Home button keeps its usual job. Pressing a key switches back to the normal layout (unless set to Always).
- **Release focus fixed for controllers.** Windows refuses focus changes from apps that didn't get the last input, and a controller press doesn't count. PiP Studio now unlocks this with an empty mouse event (no movement) before taking focus from the game, and checks again a few times if the game grabs focus back.

### Build 2.9

- **Preview before menu.** When you make the window narrower, the navigation menu now collapses to icons first, and the preview panel on the Windows page is the last thing to go. The menu still opens as an overlay from the hamburger button.

### Build 2.8

- **Auto as its own option.** Auto now sits above the Windows table as a separate first option, with its full description, instead of being squeezed into the Name column.
- **Tables that fit.** As the window narrows, the Windows and Running tables drop their least important columns, then the details panel, so the Name column never gets cut off.
- **Smoother resizing.** The live preview hides while you drag the window edge and comes back when you let go, and text is snapped to whole pixels so it no longer shimmers.

### Build 2.7

- **Auto skips minimized and closed windows.** If the window Auto would switch to is minimized or closed, it moves to the next available window instead. If the window it's showing gets minimized, it moves on too.
- **Smooth scrolling.** Settings pages and the Windows and Running lists now scroll with an eased glide, like Windows 11 apps, instead of jumping. Jumping to a search result glides there too. Turning off animations in Settings brings back the normal scrolling.

### Build 2.6

- **Auto in the window cycle.** The previous and next window shortcuts (Ctrl+Alt+[ and ], LB/RB) now go through Auto too: Auto, then each open window, then back to Auto. Picking a window this way turns Auto off for that PiP.
- **Auto explained.** The Auto row in the Windows list now says what it does: it follows the window you last used.
- **Move arrows off by default.** The arrow buttons on a PiP's hover controls are now off by default, including on your current install. Turn them back on in Settings.

### Build 2.5

- **Border color.** The Border settings now have their own color picker, in place of the old letterbox color. Turn off "Match accent color" and pick any color. A custom color is shown exactly as picked and brightens slightly on hover.
- **Messages under the PiP.** The name that pops up when you switch a PiP's source, and other quick messages, now show in a small caption below the PiP instead of over the middle of the picture. If there's no room below, it goes above. You can turn this off in Settings > Appearance.

### Build 2.4

- **Auto mode.** The top row of the Windows list is **Auto**. It starts on the first available window, then always switches to the window you were last using, even when "Follow the window I just left" is off in Settings. If its window closes, it moves to the next available one. You can also turn Auto on or off from a PiP's Source menu. Picking a specific window or display turns Auto off for that PiP.
- **No corners or borders by default.** PiPs are now square with no outline, and the Windows system border is removed too. Existing settings are migrated once, so this applies to your current install as well. You can turn either back on in Settings > Appearance.

### Build 2.1: Windows 11 design and motion

- **Navigation.** The rail follows the Windows 11 NavigationView specs: 36 px items, 4 px corners, hover fades, and an accent pill that slides and stretches between pages. It compacts below 1008 px wide, and the search box sits at the top of the rail as in Settings.
- **Content layer.** Pages sit on a lighter layer with a rounded top-left corner over Mica, as in every Windows 11 NavigationView app. The title bar shows the app icon and name.
- **Settings.** Settings use SettingsCard and SettingsExpander layouts: 4 px corners, 68 px cards, 20 px icons, and expanders that open with a smooth height and chevron animation. Shortcuts and controller buttons are grouped into expanders.
- **Tables.** The Windows and Running PiPs tables follow Task Manager: plain rows on the layer, sortable column headers with chevrons, a command bar beside the page title, fading hover, and a selection pill that grows in.
- **Motion everywhere.** Pages slide up and fade in, and settings cards arrive one after another. Hover and press states fade at Windows' own timings (83 ms in, 167 ms out). Slider thumbs grow on hover, and the live preview fades in.
- **Quick picker.** Ctrl+Alt+P now opens a Fluent window with acrylic on Windows 11, Alt-Tab–style cards with live thumbnails that fade in after the cards rise into place, search as you type, and arrow-key navigation.
- **Menus.** Menus now use 8 px corners, like Windows 11's own context menus.
- **Animation setting.** Turn off "Animation effects" in Appearance to switch off all motion.

### Build 2.0

### From UniversalPiP
- **Controller support.** Works with Xbox (including the Guide button), PlayStation, Switch and generic controllers. Every action can be bound to a button or combination, set to fire on a press, a double press or a hold. Button names match the controller family (✕ ○ □ △ for PlayStation and so on).
- **Controller controls on/off.** View + Menu (Share + Options) toggles them, and B turns them off. While they're on, the PiP gets an accent outline that flashes on each input, and button hints appear on the PiP. Optionally, the game loses keyboard focus so it ignores the controller, and gets it back when you're done.
- **Eight snap spots.** Each display has four corners plus the middle of each edge. The arrow-key shortcuts, the D-pad and the arrow buttons on the hover controls move the PiP one spot at a time, and past the edge it hops to the next display. You can lock it to one display.
- **Move out of the way.** The PiP slides to the other side when the pointer reaches it. Hold Ctrl to grab it instead.
- **Follow the window you just left.** When you switch windows, the PiP switches to the one you were on, like Alt-Tab picture-in-picture.
- **Previous and next window.** Flip a PiP's source through your open windows (LB/RB, Ctrl+Alt+[ and ]). For a whole-display PiP, this flips through your displays.
- **Frozen-picture fix.** Chromium apps such as Chrome, Edge, Spotify and Discord stop drawing while another window covers them. PiP Studio detects these apps and can restart them with the fix. It can also add the fix to the app's desktop, Start menu and taskbar shortcuts so it's always on.
- **Admin-window handling.** If a window belongs to an app running as administrator, PiP Studio explains why it can't show it and offers to restart itself as administrator.
- **Display changes.** PiPs stay on screen when you plug in or unplug a display.
- **Memory.** Closing the main window drops it from memory, and PiP Studio gives idle memory back to Windows while it sits in the tray.
- **Settings search.** Press Ctrl+F to search settings and jump straight to a match.
- **Right-click action.** Choose what right-clicking a PiP does: show the menu, close it, bring its source to the front, theater mode, or nothing.

### Windows-native look
- **Main window.** Built with WPF-UI: Mica backdrop, a collapsible navigation rail and Windows 11 settings cards. It follows your Windows light/dark setting and accent color, or you can choose your own.
- **Windows page.** A Task Manager–style table with icons, sortable columns, search, a command bar and a live preview of the selected window.
- **Running PiPs page.** A table of open PiPs showing their position, size, opacity and mode, with commands to close, crop, change source, follow, and more.
- **Menus.** The tray and PiP menus are rounded and roomy like Windows 11's, and follow light/dark mode.
- **Picker.** The quick picker (Ctrl+Alt+P) also follows the theme.
