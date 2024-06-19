<div align="center">

# Custom Object Sync

[![Generic badge](https://img.shields.io/github/downloads/VRLabs/Custom-Object-Sync/total?label=Downloads)](https://github.com/VRLabs/Custom-Object-Sync/releases/latest)
[![Generic badge](https://img.shields.io/badge/License-MIT-informational.svg)](https://github.com/VRLabs/Custom-Object-Sync/blob/main/LICENSE)
[![Generic badge](https://img.shields.io/badge/Unity-2019.4.31f1-lightblue.svg)](https://unity3d.com/unity/whats-new/2019.4.31)
[![Generic badge](https://img.shields.io/badge/SDK-AvatarSDK3-lightblue.svg)](https://vrchat.com/home/download)

[![Generic badge](https://img.shields.io/discord/706913824607043605?color=%237289da&label=DISCORD&logo=Discord&style=for-the-badge)](https://discord.vrlabs.dev/)
[![Generic badge](https://img.shields.io/endpoint.svg?url=https%3A%2F%2Fshieldsio-patreon.vercel.app%2Fapi%3Fusername%3Dvrlabs%26type%3Dpatrons&style=for-the-badge)](https://patreon.vrlabs.dev/)

Sync objects across the network with custom range, precision, and parameter usage.

![Preview](https://github.com/VRLabs/Custom-Object-Sync/assets/76777936/a31c61de-34c5-47ab-9be1-05602cd5d8ba)

### ⬇️ [Download Latest Version](https://github.com/VRLabs/Custom-Object-Sync/releases/latest)


### 📦 [Add to VRChat Creator Companion](https://vrlabs.dev/packages?package=dev.vrlabs.custom-object-sync)

</div>

---

## How it works

* Contacts and Physbones read the location and rotation of your object(s).
* Parameter Drivers convert this location and rotation into boolean values.
* Those boolean values are synced over the network in multiple steps. Choosing more steps means we can use fewer parameters.
* Once the values arrive at the remote side, they get converted back into floats.
* These floats get used to set the object back in its place on the remote side.
* This can be useful when:
  * You want to late sync a world drop.
  * You want to fly an object around and have it sync reliably.
  * You have an object that moves based on some local-only/fps dependent mechanism.

## Install guide

https://github.com/VRLabs/Custom-Object-Sync/assets/76777936/4d12a815-bb1f-4c03-b0fc-83f9ea98638a

* Click `VRLabs -> Custom Object Sync` at the top of the screen.
* Drag the object you want to sync into the `Objects to Sync` field.
* Adjust the values until you're happy with them:
  * Add Local Debug View: Adds a toggle to view the object's remote position and rotation. 
  * Quick Sync:
    * Position Precision: The position precision to be synced. Since this sync mode syncs floats, Position Precision also affects Range, and Rotation Precision is locked at 1 float per axis.
  * Non Quick Sync:
    * Position Precision: Precision of the synced object's Position.
    * Rotation Precision: Precision of the synced object's Rotation.
    * Radius: Radius of the sync. For more information, see `Sync Type`
    * Bits per Sync: Amount of bits per sync step. Lower bits means longer before the full object is synced, but also less parameter usage.
  * Enable Rotation Sync: Whether or not rotation should be Synced.
  * Sync Type: Whether or not the sync should be based on avatar root or on world origin.
    * Avatar Centered: The sync is based off of a point which is dropped when-ever you start the sync. This means you can use way lower range, but also it won't late sync.
      * This is forced for quick sync.
    * World Centered: The sync is based off of world origin. This means you'll need a big range to support big worlds, but it will late sync.
  * Add Damping Constraint to Object: Whether or not the remote object should be damped. This means the object moves to its new position smoothly whenever it receives a new position.
    * Damping value: How the damping behaves: 0 = don't move at all. 1 = move to the new position very fast.
* Press Generate Custom Sync

## How to use

* Enable the `CustomObjectSync/Enabled` bool to start the sync.
* Now the location of the Target objects will be synced over the network.

## Performance stats

Rotation Sync:
```c++
Constraints:            11-13 + 3 per object 
Contact Receivers:      6
Contact Senders:        3
FX Animator Layers:     2 + 2 per object
Phys Bones:             6
Phys Bone Colliders:    3
Rigidbodies:            1
Joints:                 1
Expression Parameters:  1-256
```

No Rotation Sync:
```c++
Constraints:            11-13 + 3 per object 
Contact Receivers:      6
Contact Senders:        3
FX Animator Layers:     2 + 1 per object
Rigidbodies:            1
Joints:                 1
Expression Parameters:  1-256
```

## Hierarchy layout

Rotation Sync:
```html
Custom Object Sync
|-Target
|-Measure
|  |-Position
|  |  |-SenderX
|  |  |-SenderY
|  |  |-SenderZ
|  |  |-Receiver X+
|  |  |-Receiver X-
|  |  |-Receiver Y+
|  |  |-Receiver Y-
|  |  |-Receiver Z+
|  |  |-Receiver Z-
|  |-Rotation
|  |  |-Measure Bones
|  |  |  |-Measure X Magnitude
|  |  |  |-Measure X Sign
|  |  |  |-Measure Y Magnitude
|  |  |  |-Measure Y Sign
|  |  |  |-Measure Z Magnitude
|  |  |  |-Measure Z Sign
|  |  |-Measure Planes
|  |  |  |-X Angle Plane
|  |  |  |-Y Angle Plane
|  |  |  |-Z Angle Plane
|-Set
|  |-Result
```

No Rotation Sync:
```html
Custom Object Sync
|-Target
|-Measure
|  |-Position
|  |  |-SenderX
|  |  |-SenderY
|  |  |-SenderZ
|  |  |-Receiver X+
|  |  |-Receiver X-
|  |  |-Receiver Y+
|  |  |-Receiver Y-
|  |  |-Receiver Z+
|  |  |-Receiver Z-
|-Set
|  |-Result
```

## Contributors

* [jellejurre](https://github.com/jellejurre)

## License

Custom Object Sync is available as-is under MIT. For more information see [LICENSE](https://github.com/VRLabs/Custom-Object-Sync/blob/main/LICENSE).

​

<div align="center">

[<img src="https://github.com/VRLabs/Resources/raw/main/Icons/VRLabs.png" width="50" height="50">](https://vrlabs.dev "VRLabs")
<img src="https://github.com/VRLabs/Resources/raw/main/Icons/Empty.png" width="10">
[<img src="https://github.com/VRLabs/Resources/raw/main/Icons/Discord.png" width="50" height="50">](https://discord.vrlabs.dev/ "VRLabs")
<img src="https://github.com/VRLabs/Resources/raw/main/Icons/Empty.png" width="10">
[<img src="https://github.com/VRLabs/Resources/raw/main/Icons/Patreon.png" width="50" height="50">](https://patreon.vrlabs.dev/ "VRLabs")
<img src="https://github.com/VRLabs/Resources/raw/main/Icons/Empty.png" width="10">
[<img src="https://github.com/VRLabs/Resources/raw/main/Icons/Twitter.png" width="50" height="50">](https://twitter.com/vrlabsdev "VRLabs")

</div>

