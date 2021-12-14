# FlyingCarpet for Rust (Remod original)

## Overview
**Flying Carpet for Rust** is an Oxide plugin which allows an enabled user to spawn and ride their own flying machine.  The carpet consists of a floor rug, chair, code lock, and lantern.  The lantern is used to take off and land.

There are two modes of operation depending on the permission granted to the user.  The default mode requires low-grade fuel in the lantern in order to fly.  The unlimited mode does not require fuel.

For the default mode, the user will receive notification via chat message as well as an audible water pump sound when fuel is low (1 low grade fuel).  Each unit of low grade fuel gives you 10 minutes of flying time, which is the same rate of usage as the standard lantern.  When you run out of fuel, the carpet will land itself immediately.

![](https://i.imgur.com/ZsXcSLp.png)

It is recommended that you use this in conjunction with https://github.com/WheteThunger/ParentedEntityRenderFix until such time that Facepunch fixes the culling issue impacting plugins built from multiple parented objects.  I would not hold my breath that they will since this was likely done for overall performance and suits vanilla just fine.  See more below.

## Permissions

* flyingcarpet.use -- Allows player to spawn and fly a carpet using low grade fuel
* flyingcarpet.unlimited -- Removes the fuel requirement

It is suggested that you create groups for each mode:
* oxide.group add fc
* oxide.group add fcunlimited

Then, add the associated permissions to each group:
* oxide.grant group fc flyingcarpet.use
* oxide.grant group fcunlimited flyingcarpet.unlimited

Finally, add users to each group as desired:
* oxide.usergroup add rfc1920 fc

Of course, you could grant, for example, unlimited use to all players:
* oxide.grant group default fc.unlimited

## Chat Commands

* /fc  -- Spawn a flying carpet
* /fcd -- Despawn a flying carpet (must be within 10 meters of the carpet)
* /fcc -- List the current number of carpets (Only useful if limit set higher than 1 per user)
* /fchelp -- List the available commands (above)

## Configuration
Configuration is done via the FlyingCarpet.json file under the oxide/config directory.  Following is the default:
```json
{
  "AllowLantern": false,
  "AllowRepaint": true,
  "UseMaxCarpetChecks": true,
  "DoubleFuel": false,
  "NameOnSign": true,
  "PlayEmptySound": false,
  "RequireFuel": true,
  "MaxCarpets": 1,
  "VIPMaxCarpets": 2,
  "MinDistance": 10.0,
  "MinAltitude": 5.0,
  "NormalSpeed": 12.0,
  "SprintSpeed": 25.0,
  "ChairSkinID": 943293895,
  "RugSkinID": 871503616,
  "Version": {
    "Major": 1,
    "Minor": 1,
    "Patch": 7
  }
}
```

  - `AllowLantern` -- Allow lantern use while not seated (false)
  - `AllowRepaint` -- Allow repainting of sign: (true)
  - `UseMaxCarpetChecks` -- Deploy - Enable limited FlyingCarpets per person (true)
  - `DoubleFuel` -- Double fuel consumption (false)
  - `PlayEmptySound` -- Play low fuel sound (true)
  - `RequireFuel` -- Require Fuel to Operate (true)
  - `MaxCarpets` -- Deploy - Limit of Carpets players can build (1)
  - `VIPMaxCarpets` -- Deploy - Limit of carpets VIP players can build (2)
  - `MinDistance` -- Minimum distance for /fcd (10)
  - `MinAltitude` -- Minimum Flight Altitude (5.0)
  - `NormalSpeed` -- Normal Flight Speed (12.0)
  - `SprintSpeed` -- Sprint Flight Speed (25.0
  - `ChairSkinID` -- 943293895
  - `RugSkinID` -- 871503616

Note that that owner/admin can customize the skins for both the chair and the rug, set global fuel requirements and flying speed, and limit the number of carpets for each player (highly recommended).

You *could* set "RequireFuel" to false, but it is recommended that you leave this setting true and use the flyingcarpet.unlimited permission instead if you want to remove the fuel requirement.

For the ParentedRenderFix plugin, you should use the following at a minimum:

```json
{
  "EnabledEntities": [
    "chair.deployed",
    "woodbox_deployed",
    "lantern.deployed",
    "rug.deployed"
  ]
}
```

## Flight School
1. Type /fc to spawn a carpet.
2. Jump on the carpet and set a code on the lock.  Unlock after setting the code.
2. Add low-grade fuel to the lantern (if running in default mode).
3. Sit in the chair.
4. Aim at the lantern and press 'E' to take off!
5. From here on use, WASD, Shift (sprint), spacebar (up), and Ctrl (down) to fly.
6. When ready to land, point at the lantern and press E again.
7. Once on the ground, use the spacebar to dismount.
8. Lock the carpet using the code lock to prevent others from using it.
9. Use /fcd while standing next to the carpet to destroy it.


