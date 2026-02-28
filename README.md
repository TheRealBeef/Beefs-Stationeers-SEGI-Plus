# Beef's SEGI Plus

<p align="center" width="100%">
<img alt="SEGI Plus Logo" src="./About/thumb.png" width="45%" />
</p>

There is an in-game config menu with F11.

A modified version of SEGI (Sonic Ether Global Illumination) for Stationeers with shader fixes and performance options.

## Features

- 4 quality presets (Low/Medium/High/Extreme)
- High Density Mode option at High/Extreme quality for twice the detail at half the range
- Lightweight Mode that voxelizes only emissive objects for maximum performance at the cost of more light leakage
- This lightweight mode is independent from the quality preset, so can be enabled/disabled to find the best balance for you
- Emissive Light Gain to control emissive brightness separately from overall GI
- Emissive Bubble option to prevent held items and suit from contributing to GI
- Automatic day/night ambient lighting that adjusts based on sun position
- Modified SEGI shaders to work properly with Stationeers rendering
- In-game configuration menu (Press F11 while in-game)
- Adaptive performance mode with strategy and target framerate options

## Requirements

**WARNING:** This is a StationeersLaunchPad Plugin Mod. It requires BepInEx to be installed with the StationeersLaunchPad plugin.

See: [https://github.com/StationeersLaunchPad/StationeersLaunchPad](https://github.com/StationeersLaunchPad/StationeersLaunchPad)

## Installation

1. Ensure you have BepInEx and StationeersLaunchPad installed.
2. Install from the Steam Workshop, or manually place the DLL file into your `/BepInEx/plugins/` folder.

## Usage

Configuration available through F11 in-game menu, StationeersLaunchPad config, or BepInEx config files.

## Credits

Built upon the work of:
- **Sonic Ether** (original SEGI): [https://github.com/sonicether/SEGI](https://github.com/sonicether/SEGI)
- **Erdroy** (initial Stationeers port): [https://github.com/Erdroy/Stationeers.SEGI](https://github.com/Erdroy/Stationeers.SEGI)
- **Vinus** (previous implementation): [https://github.com/TerameTechYT/StationeersSharp/tree/development/Source/SEGIMod](https://github.com/TerameTechYT/StationeersSharp/tree/development/Source/SEGIMod)

## Changelog
>### Version 1.4.0:
>- Major rendering pipeline rewrite (again), likely you will want to revisit which settings you use (again)
>- Less ghosting/noise
>- Added Emissive Light Gain setting to control emissive brightness separately from overall GI
>- Added Emissive Bubble toggle to prevent held items and suit from glowing
>- Added High Density Mode option at High/Extreme quality — twice the detail but half the range
>- Fix some objects falsely detected as emissive
>- Adaptive performance now takes 15 seconds between changes
>- Removed Day/Night Ambient Brightness sliders, now handled properly and automatically
>- Removed Near Light Gain as it is ugly, replaced by Emissive Light Gain which controls emissive surfaces separate from sun contribution
>- Secondary Bounce Gain is capped lower to prevent runaway brightness explosions

>### Version 1.3.1:
>- Hotfix to reduce artifacting/visual "snow"

>### Version 1.3.0:
>- Major performance improvements, likely you will want to revisit which settings you use
>- Lightweight mode likely has little performance improvement when enabled, adaptive performance with reduce distance first strategy is likely the ideal for most cases

>### Version 1.2.6:
>- Non-emissive geometry now cached and re-used across frames. Scrolled with camera movement
>- Scene geometry batched across 8 frames
>- Emissive geometry is rendered per-frame and merged with cached non-emissive geometry
>- Sun shadow geometry is updated every 120 frames instead of per-frame
>- Fix issue disabling/re-enabling SEGI Plus in config menu

>### Version 1.2.5:
>- Minor performance improvements, primarily caching and reusing data where possible

>### Version 1.2.4:
>- Fix glowing robots, they no longer glow wildly

>### Version 1.2.3:
>- Another pass on adaptive performance
>- Only two strategies now: Balanced and Reduce Distance first
>- Added min distance option for reduce distance first
>- Properly handle when desired framerate is set above in-game framerate limiter

>### Version 1.2.2:
>- Another pass on adaptive performance
>- Only two strategies now: Balanced and Reduce Distance first
>- Added min distance option for reduce distance first
>- Properly handle when desired framerate is set above in-game framerate limiter

>### Version 1.2.2:
> - Shrunk F11 menu slightly
> - Added color backgrounds to each section to improve understanding of grouping as it's getting crowded
> - Added Adaptive Strategy option to adjust what's prioritized in adaptive performance mode
> - Added long-term accumulator for adaptive to bump quality up slightly when framerate stays stable but slightly below target

>### Version 1.2.1:
> - Widened adaptive framerate slider choices
> - Automatically remove/mark read the major update popup if go into world
> - Added an x10 multiplier option if you want to play around with silly gain values
> - Darkened background of F11 menu slightly

>### Version 1.2.0:
> - Added first pass of adaptive framerate control that works with the quality setting to try and improve performance
> - This can be used at any quality setting and with or without lightweight mode
> - This isn't automatically enabled as it's yet experimental - you can enable this in settings

>### Version 1.1.1
> - Improved lightweight mode cleanup
> - I inverted the new sun calc like a big dumb

>### Version 1.1.0
>- Added more info to the F11 menu to help with understanding settings
>- Fixed F11 menu breaking when returning or used in main menu/splash screens
>- Added F11 menu scaling so it is bigger at 1440/4k
>- Better day/night transition (now transitions between 2-10 degrees sun elevation instead of +5/-5 deg)
>- Added Advanced Furnace to object exclusion list in lightweight mode
>- Thanks **BassManDan** for your feedback helping get this update done quickly 

>### Version 1.0.0
>- Updated plugin architecture for current Stationeers version
>- Added lightweight rendering with cached object culling
>- Implemented quality preset system
>- Improved scene management and layer culling
>- Day/night diffuse light adjustments
>- Updated SEGI shaders for Stationeers compatibility

## Roadmap
- [ ] add more text explanations if needed
- [ ] see if i can cull out the interaction boxes because these are annoying when they glow
- [ ] check sensor lenses/nvgs/etc and see if there's anything there to cull too
- [ ] find what other items need to be culled in lightweight mode since they're giant beacons of light

## Source Code

The source code is available on GitHub:
[https://github.com/TheRealBeef/Beefs-Stationeers-SEGI-Plus](https://github.com/TheRealBeef/Beefs-Stationeers-SEGI-Plus)