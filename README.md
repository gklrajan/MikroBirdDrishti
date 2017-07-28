# MikroBirdDrishti v2.10

MikroBird Drishti a high-speed image acquisition program written on C# which combines a MIKROTRON MC4082 camera 
with a FIREBIRD frame grabber board of Active Silicon. Drishti is a Sanskrit term for vision.

This program  uses a direct memory buffering wherein physical addresses in the system memory are locked 
by the gramegrabber board prior to acquisition and the grabber board uses vitual addressing to directly
transfer the images to the system buffers. The program in its current default mode uses 2 ping-pong buffers.

* The number of buffers, number of images per buffer, region of interest, acquisition frame rate and exposure time 
settings can be directly changed from within the program; highlighted in the begining (see somments w/ code).

* The program also prints a realtime camera frame rate and system acquisition frame rate in fps, along with number of each system acquired frame with its camera timestamp.

* All parameters are stored in a csv file and saved to disk in the following order:  CameraTime, SystemTime, CameraFrameNumber, CameraFrameRate, AcquisitionFrameRate, ROIx X ROIy.

* ^Images can be saved to disk. This uses EmguCV (openCv wrapper for c#).

* ^Images can be also stiched into a video with the already selected frame rate and saved to disk. This uses ffmpeg library.

^needs to be selected before every acquisition. Not selected by default as disk writing is time consuming and can lead to reduction 
in recorded framerate (w/ lost frames in the buffer).
        
This code is adapted from the examples provided with the phoenix software developement kit (SDK). 

More info: Gokul Rajan (gklrajan@gmaildotcom), DEL-BENE Lab, Paris. 26-07-2017.

to do: add writing to linkspeed cam register. currently throws a device not ready error.
