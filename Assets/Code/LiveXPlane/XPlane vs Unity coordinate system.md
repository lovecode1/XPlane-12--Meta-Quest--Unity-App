
# Unity vs X Plane coordinate system

## X Plane:

Link: https://developer.x-plane.com/article/screencoordinates/

### 3D coordinate system:
The origin 0,0,0 is on the surface of the earth at sea level at some “reference point”.
The +X axis points east from the reference point.
The +Z axis points south from the reference point.
The +Y axis points straight up away from the center of the earth at the reference point.


### Aircraft Coordinates
The X axis points to the right side of the aircraft.
The Y axis points up.
The Z axis points to the tail of the aircraft.

You can draw in aircraft coordinates using these OpenGL transformations:

```
    glTranslatef(local_x, local_y, local_z);
    glRotatef	(-heading, 0.0,1.0,0.0);	
    glRotatef	(-pitch,-1.0,0.0,0.0);
    glRotatef	(-roll, 0.0,0.0,1.0);
```

where local_x, local_y, and local_z is the plane’s location in “local” (OpenGL) coordinates, and pitch, heading, and roll are the Euler angles for the plane. Be sure to use glPushMtarix and glPopMatrix to restore the coordinate system.

You can manually transform a point from airplane to world coordinates using the following formula:

```
    INPUTS: (x_plane,y_plane,z_plane) = source location in airplane coordinates.  
            phi = roll, psi = heading, the = pitch.  
            (local_x, local_y, local_z) = plane's location in the world 
    OUTPUTS:(x_wrl, y_wrl, z_wrl) = transformed location in world.
    x_phi=x_plane*cos(phi) + y_plane*sin(phi)
    y_phi=y_plane*cos(phi) - x_plane*sin(phi)
    z_phi=z_plane
    x_the=x_phi
    y_the=y_phi*cos(the) - z_phi*sin(the)
    z_the=z_phi*cos(the) + y_phi*sin(the)
    x_wrl=x_the*cos(psi) - z_the*sin(psi) + local_x
    y_wrl=y_the                           + local_y
    z_wrl=z_the*cos(psi) + x_the*sin(psi) + local_z
```

This is in fact 3 2-d rotations plus an offset.

## Unity

Link: https://docs.unity3d.com/6000.2/Documentation/Manual/QuaternionAndEulerRotationsInUnity.html

Axis Directions:
    X-axis: Represents the horizontal direction, with positive X pointing to the right.
    Y-axis: Represents the vertical direction, with positive Y pointing upwards.
    Z-axis: Represents the depth or forward direction, with positive Z pointing forward (into the screen from a standard camera perspective).