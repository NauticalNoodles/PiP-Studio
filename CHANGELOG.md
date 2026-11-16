# PiP Studio changelog

## 1.0.4

Game on: PiP Studio's window can now look like your Xbox.

- New **Window style** option in Settings > Appearance: Windows 11 or Xbox
- The Xbox style is dark with Xbox green highlights, rounder tiles, and buttons that light up green with a white outline when you reach them with a controller or the keyboard
- It goes great with the controller-friendly mode
- Only the main window changes; your PiPs and menus look the same as before

Marking windows as PiPs is clearer and easier to undo:

- "Treat as a PiP" now asks first and explains what it does (the window stays on top, snaps to PiP spots and follows your PiP shortcuts)
- Every window you marked is listed under **Marked as PiPs** on the Running PiPs page, with a Remove button for each (or Remove all)
- Removing a mark makes it a normal window again and takes it off always on top
- Other apps' PiPs stay on the Running PiPs page while they're minimized, with a Restore button to bring them back
- The title bar on other apps' PiPs no longer has a maximize button

Control PiP Studio from your phone:

- New **Phone companion** in Settings > General: turn it on, press **Pair a phone**, scan the QR code, and type the PIN your phone shows (just like pairing Moonlight with Sunshine)
- It opens in your phone's browser (Android or iPhone, nothing to install) and shows every PiP, yours and other apps', with buttons to move, resize, minimize, restore and close them, plus opacity and volume sliders
- Under your PiPs, **App volume** works like Windows' volume mixer: a slider and mute button for every app playing sound, with the apps you actually hear at the top and quiet ones at the bottom: an app that starts playing jumps to the top, the rest re-sorts calmly, and nothing moves while you're using a slider
- Pin the apps you always want at hand to the top of the volume list
- Tap **Install app** to put it on your home screen, where it opens full screen like a real app
- A clean, dark look after Apple's Liquid Glass (glass panels with lit edges, glass capsule sliders that turn clear while you drag): each PiP shows the buttons you use most with its volume and opacity underneath, and the rest under **More** (it goes solid if your phone has Reduce transparency on)
- Every PiP and every app in the volume list shows the app's own icon
- Turn on **Preview** (under More) to see what a PiP is showing, live on your phone
- **Position** puts a PiP on any corner or edge spot with one tap, **Window** switches which window it shows, and **Follow** turns "follow the window I just left" on or off
- It only works on your home network, only for phones you've paired, and everything between your phone and PC is encrypted. You can remove a phone any time

Crash reports:

- If PiP Studio ever crashes, it notices the next time it starts and offers to send a crash report. It opens a bug report on GitHub with the details filled in, so you can see exactly what's sent before you send it
- **Report a problem** in Settings > General > About does the same for anything else that isn't working right

Behind the scenes:

- Settings and your open PiPs are saved more safely, so a crash or power cut can't wipe them

## 1.0.3

Other apps' PiPs get the PiP Studio treatment when you point at them.

- Hover over the PiP window of Chrome, Firefox, Discord or another app and PiP Studio's title bar pops up on top of it
- Drag the bar to move the PiP (it snaps into place when you let go), or scroll on it to change the volume
- It has minimize, maximize and mute buttons, plus the buttons you picked for your own PiPs' title bar: show the app, theater mode, bigger, smaller, next corner, next display and close
- Minimized one that has no taskbar button? Bring it back from PiP Studio's tray menu
- Every title bar now has buttons to make the PiP bigger or smaller (you can turn them off in Settings > Appearance > Title bar buttons)
- Settings > Appearance > Title bar buttons shows a preview of your title bar that updates as you turn buttons on and off or move them
- "Follow the window I just left" is now right in a PiP's menu instead of tucked away under Source
- Don't want it? Turn off "Show controls on other apps' PiPs" in Settings > Behavior

## 1.0.2

PiP Studio can now update itself! No more downloading new versions by hand.

- It checks for a new version once a day and lets you know when one is out
- Click **Update now** in Settings > General > Updates, and it downloads the update and restarts on the new version, with your PiPs right where they were
- Don't want it to check? Turn off "Check for updates automatically" in the same place
- The Microsoft Store version keeps getting its updates from the Store

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
