# MikroBirdDrishti v2.10
MikroBird Drishti a high-speed image acquisition program written on C# which combines a MIKROTRON MC4082 camera 
with a FIREBIRD frame grabber board of Active Silicon. Drishti is a Sanskrit term for vision.

This program  uses a direct memory buffering wherein physical addresses of the system memory buffers are locked 
by the frame grabber board prior to acquisition and the grabber board uses vitual addressing to directly
transfer the images to the system buffers. The program in its current default mode uses 2 ping-pong buffers.

* The number of buffers, number of images per buffer, region of interest, acquisition frame rate and exposure time 
settings can be directly changed from within the program; highlighted in the begining (see somments w/ code).

* The program also prints a realtime camera frame rate and system acquisition frame rate in fps, along with number of each system acquired frame with its camera timestamp and system timestamp. This is very useful for troubleshooting.

* All above parameters are also stored in a csv file and saved to disk in the following order:  CameraTime, SystemTime, CameraFrameNumber, CameraFrameRate, AcquisitionFrameRate, ROIx X ROIy.

* ^Images can be saved to disk. This uses EmguCV (openCv wrapper for C#).

* ^Images can be also stiched into a video with the already selected frame rate and saved to disk. This uses the ffmpeg library.

^needs to be selected before every acquisition. Not selected by default as disk writing is time consuming and can lead to reduction in recorded framerate (w/ lost frames in the buffer).
        
This code is adapted from the examples provided with the phoenix software developement kit (SDK). 

More info: Gokul Rajan (gklrajan@gmaildotcom), DEL-BENE Lab, Paris. 26-07-2017.

Additional notes: This was originally written with Phoenix SDK (v01.04.00) to integrate an Active Silicon FireBird frame grabber board (P/N: MP-FBD-4XCXP6-2PE8) and a Mikrotron camera (MC4082) running on FW V0.27.033F0.38.925.

To-do: add writing to linkspeed cam register. Currently throws a device not ready error.
