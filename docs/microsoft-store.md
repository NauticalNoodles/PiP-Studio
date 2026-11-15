# Publishing to the Microsoft Store

PiP Studio goes to the Store as an MSIX package. The Store signs it for free, installs and updates it, and Store installs don't show the SmartScreen warning. The installer and portable zip on GitHub stay as they are.

## One-time setup

1. **Create a developer account** (free) at https://storedeveloper.microsoft.com and finish the ID check.
2. In **Partner Center > Apps and games > New product > MSIX or PWA app**, reserve the name **PiP Studio**.
3. Open the app > **Product management > Product identity** and copy three values:
   - `Package/Identity/Name` (looks like `12345NauticalNoodles.PiPStudio`)
   - `Package/Identity/Publisher` (looks like `CN=ABCD1234-...`)
   - `Package/Properties/PublisherDisplayName` (`NauticalNoodles`)
4. On GitHub, open the repo > **Settings > Secrets and variables > Actions > Variables** and add three repository variables with those values:
   - `MSIX_IDENTITY_NAME`
   - `MSIX_PUBLISHER`
   - `MSIX_PUBLISHER_DISPLAY_NAME`

From then on, every release also builds `PiPStudio-<version>-Store.msix`.

## Each release

1. Publish the GitHub release as usual.
2. When the **Release** workflow finishes, open that run on the Actions tab and download the **PiPStudio-<version>-Store** artifact (a zip with the `.msix` inside).
3. In Partner Center, start a new submission, upload the `.msix` under **Packages**, and submit. Certification usually takes a few days.

## First submission: what Partner Center asks for

- **Properties:** category *Utilities & tools*. Privacy policy URL: `https://github.com/NauticalNoodles/PiP-Studio/blob/main/PRIVACY.md`.
- **Age ratings:** fill in the questionnaire; the app has no user content or online features.
- **Store listing:** use the text below and at least one screenshot (1920×1080 or 1366×768 work).
- **Submission options > notes for certification:** paste the restricted capability note below. The Store asks why the app needs `runFullTrust`.

### Restricted capability note (runFullTrust)

> PiP Studio is a desktop app (.NET 8, WPF/WinForms). It needs runFullTrust to list other open windows and show live thumbnails of them with the Desktop Window Manager API, to move and resize picture-in-picture windows that other apps create, to register global keyboard shortcuts, and to read game controllers. It doesn't collect any data or connect to the internet.

### Store listing text

**Short description**

Picture-in-picture for any window, screen or part of the screen.

**Description**

Keep a video, a stream, a guide or a chat on screen while you play or work. PiP Studio puts any window, a whole display or just part of the screen into a small floating picture-in-picture window that stays on top.

- Works with any app, a whole screen, or a region you pick
- Also controls the picture-in-picture windows of your browser, Discord and other apps
- Snaps neatly into corners and edges, and gets out of the way when your mouse comes near
- Fully usable with an Xbox, PlayStation or Switch controller
- 8 sizes, opacity, volume, crop and theater mode for each PiP
- Light and dark themes that match Windows

PiP Studio is free and open source.

**Keywords:** picture in picture, pip, floating window, always on top, overlay, mini player, controller

## Notes

- **Settings are separate.** The Store version keeps its settings in its own storage, so settings from the installer or portable version don't carry over.
- **No admin restart.** The Store version can't restart itself as administrator, so that option is hidden there.
- **Freeze-fix shortcuts.** The "Always start it with the fix" option edits desktop and Start menu shortcuts of other apps. In the Store version it can only reach desktop shortcuts.
