# X Plane 12 - Meta Quest 3 - Unity App 

This is the Meta Quest 3 app to connect to X Plane 12 on MacOS

## Unity version I used:
6000.2

Most likely newer version should work just fine, most likely some older version mights also work fine.

## X Plane 12 plugin:
https://github.com/lovecode1/XPlane-12--Meta-Quest-Plugin

## Ideas for improvments:
(-) Use MacOS native API to capture the screen and then stream it using GPU supported codecs and then on the Meta Quest use GPU supported codecs to uncompress and render the steam.
Currently I'm using fast jpg compress/decompress on CPU not GPU. If we can move to the GPU we might see increased performance.
Since Unity only supports Vulkan I could not use available Android code that uses OpenGL.
You might find better options than what I found.
You can right a plugin for Unity that renders the OpenGL textures received from the Mac.

(-) Make Meta Quest controls visible and add menus on them to make easier to control the VR.

(-) Is there a way to support stereoscopic? 


