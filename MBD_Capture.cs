/****************************************************************************
 *
 * ACTIVE SILICON LIMITED
 *
 * File name   : phx_live.cs
 * Function    : Example simple acquisition and display application
 * Updated     : 13-Feb-2015
 *
 * Copyright (c) 2015 Active Silicon
 ****************************************************************************
 * Comments:
 * --------
 * This example shows how to initialise the frame grabber and use the Display
 * library to run live double buffered (also known as ping-pong) acquisition,
 * using a callback function.
 * It also shows how to use the image conversion function. It captures into
 * a direct buffer, and then converts the image data into a format suitable
 * for display. This reduces the amount of PCI bandwidth used.
 *
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using ActiveSilicon;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.Util;
using Emgu.CV.UI;

using System.Drawing.Imaging;
using AForge.Video.FFMPEG;

namespace phx_live
{
    class Program
    {


        /* Define an application specific structure to hold user information */
        public struct tPhxLive
        {
            public volatile uint dwBufferReadyCount;
            public volatile bool fBufferReady;
            public volatile bool fFifoOverflow;
        };

        /*
        phxlive_callback()
         * This is the callback function which handles the interrupt events.
         */
        unsafe static void phxlive_callback(
           uint hCamera,          /* Camera handle. */
           uint dwInterruptMask,  /* Interrupt mask. */
           IntPtr pvParams         /* Pointer to user supplied context */
        )
        {
            tPhxLive* psPhxLive = (tPhxLive*)pvParams;

            /* Handle the Buffer Ready event */
            if ((uint)Phx.etParamValue.PHX_INTRPT_BUFFER_READY == (dwInterruptMask & (uint)Phx.etParamValue.PHX_INTRPT_BUFFER_READY))
            {
                /* Increment the Display Buffer Ready Count */
                psPhxLive->dwBufferReadyCount++;
                psPhxLive->fBufferReady = true;
            }

            /* FIFO Overflow */
            if ((uint)Phx.etParamValue.PHX_INTRPT_FIFO_OVERFLOW == (dwInterruptMask & (uint)Phx.etParamValue.PHX_INTRPT_FIFO_OVERFLOW))
            {
                psPhxLive->fFifoOverflow = true;
            }

            /* Note:
             * The callback routine may be called with more than 1 event flag set.
             * Therefore all possible events must be handled here.
             */
        }


        /*
        phxlive()
         * Simple live capture application code, using image conversion in order to reduce
         * the amount of PCI bandwidth used.
         */
        unsafe static int phxlive(
           Phx.etParamValue eBoardNumber,        /* Board number, i.e. 1, 2, or 0 for next available */
           Phx.etParamValue eChannelNumber,      /* Channel number */
           String strConfigFileName,   /* Name of config file */
           PhxCommon.tCxpRegisters sCameraRegs          /* Camera CXP registers */


        )


        {

            ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            // SET ACQUISITION PARAMETERS HERE//
            int AcqTime = 410000; //310000 for 5.10; run 660000 for 11 mins, 960000 for 16 mins, 1260000 for 21 mins
            uint ROIx = 1024; // Max resolution is 2336x1728px for the MC4082
            uint ROIy = 1024; // lower ROI --> less data --> faster disk writing 
                              // This is not exactly ROI; basically just selecting buffer size, rest of the sent image is not catured in the buffer
                              // Real camera ROI and ROIOffset lines exist in the code; currently non-functional.
            uint OffsetX = 128;
            uint OffsetY = 128;
            uint CameraFPS = 949; // when writing to disk, this is not very imp as you will be limited by writing speed
            int VideoFPS = 182; // this can change; look at console window calFPS readout to adjust i f required
            uint CameraExp = 1052;
            ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

            //  uint CaptureMode = 1;
            //  uint ImgPerBuff = 1; 

            Phx.etStat eStat = Phx.etStat.PHX_OK;    /* Status variable */
            Phx.etParamValue eAcqType = 0;                    /* Parameter for use with PHX_ParameterSet/Get calls */
            Phx.etParamValue eParamValue = 0;
            Pbl.etPblParamValue ePblCaptureFormat = 0;
            Phx.etParamValue eCamSrcCol = 0;
            Phx.etParamValue eCaptureFormat = Phx.etParamValue.PHX_BUS_FORMAT_MONO8;
            Phx.etParamValue eCamFormat = 0;    
            uint dwBufferReadyLast = 0;                    /* Previous BufferReady count value */
            IntPtr pParamValue = IntPtr.Zero;
            IntPtr pConfigFile = IntPtr.Zero;
            PhxCommon myPhxCommon = new PhxCommon();
            Phx.PHX_AcquireCallBack PHX_Callback = new Phx.PHX_AcquireCallBack(phxlive_callback);
            Phx.stImageBuff[] asImageBuffers = null;                 /* Capture buffer array */
            uint[] ahCaptureBuffers = null;                 /* Capture buffer handle array */
            tPhxLive sPhxLive;                                       /* User defined event context */
            uint hCamera = 0;                    /* Camera handle */
            uint dwAcqNumBuffers = 0;
            uint dwBufferWidth = 0;
            uint dwBufferHeight = 0;
            uint dwBufferStride = 0;
            uint dwCamSrcDepth = 0;
            bool fCameraIsCxp = false;
            bool fIsCxpCameraDiscovered = false;

            uint hDisplay = 0;                    /* Display handle */  
            uint hDisplayBuffer = 0;

            uint dwCurrentBuffer = 0;
            uint dwLastBuffer = 0;
            uint dwCurrentValue = 0;

            int ROIxx = Convert.ToInt32(ROIx); // some padantic functions down the line would only take int arguments for ROI!
            int ROIyy = Convert.ToInt32(ROIy);
            uint LEDStatus = 0;

            var csv = new StringBuilder();

            /* Some timers for fps calculation and troubleshooting */
            System.Diagnostics.Stopwatch fpswatch4R = new System.Diagnostics.Stopwatch();
            System.Diagnostics.Stopwatch fpswatch4C = new System.Diagnostics.Stopwatch();
            System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();

            /* Create a Phx handle */
            eStat = Phx.PHX_Create(ref hCamera, Phx.PHX_ErrHandlerDefault); //PhX errorhandler removed temporarly; to manually consider frame losses during analysis
            if (Phx.etStat.PHX_OK != eStat) goto Error;

            /* Set the configuration file name */
            if (!String.IsNullOrEmpty(strConfigFileName))
            {
                pConfigFile = Marshal.UnsafeAddrOfPinnedArrayElement(PhxCommon.GetBytesFromString(strConfigFileName), 0);
                eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_CONFIG_FILE, ref pConfigFile);
                if (Phx.etStat.PHX_OK != eStat) goto Error;
            }

            /* Set the board number */
            eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_BOARD_NUMBER, ref eBoardNumber);
            if (Phx.etStat.PHX_OK != eStat) goto Error;

            /* Set the channel number */
            eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_CHANNEL_NUMBER, ref eChannelNumber);
            if (Phx.etStat.PHX_OK != eStat) goto Error;


            /* Open the board using the above configuration file */
            eStat = Phx.PHX_Open(hCamera);
            if (Phx.etStat.PHX_OK != eStat) goto Error;



            /* Set the Image ROI */
            eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_ROI_XLENGTH, ref ROIx); // ROIx and ROIy values init above
            eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_ROI_YLENGTH, ref ROIy);

            /* Read various parameter values in order to generate the capture buffers. */
            eStat = Phx.PHX_ParameterGet(hCamera, Phx.etParam.PHX_ROI_XLENGTH, ref dwBufferWidth);
            if (Phx.etStat.PHX_OK != eStat) goto Error;
            eStat = Phx.PHX_ParameterGet(hCamera, Phx.etParam.PHX_ROI_YLENGTH, ref dwBufferHeight);
            if (Phx.etStat.PHX_OK != eStat) goto Error;
            eStat = Phx.PHX_ParameterGet(hCamera, Phx.etParam.PHX_CAM_SRC_DEPTH, ref dwCamSrcDepth);
            if (Phx.etStat.PHX_OK != eStat) goto Error;
            eStat = Phx.PHX_ParameterGet(hCamera, Phx.etParam.PHX_CAM_SRC_COL, ref eCamSrcCol);
            if (Phx.etStat.PHX_OK != eStat) goto Error;
            eStat = Phx.PHX_ParameterGet(hCamera, Phx.etParam.PHX_BUS_FORMAT, ref eCaptureFormat);
            if (Phx.etStat.PHX_OK != eStat) goto Error;
            eStat = Phx.PHX_ParameterGet(hCamera, Phx.etParam.PHX_CAM_FORMAT, ref eCamFormat);
            if (Phx.etStat.PHX_OK != eStat) goto Error;
            eStat = Phx.PHX_ParameterGet(hCamera, Phx.etParam.PHX_ACQ_FIELD_MODE, ref eAcqType);
            if (Phx.etStat.PHX_OK != eStat) goto Error;
            eStat = Phx.PHX_ParameterGet(hCamera, Phx.etParam.PHX_ACQ_NUM_BUFFERS, ref dwAcqNumBuffers);
            if (Phx.etStat.PHX_OK != eStat) goto Error;

            
            ///////////////////////////////////////

            /* If you wnt to change the number of acquisition buffers, change here. 
             * Can easily acquire at the frame rate we want (~560Hz) with just 2 buffers*/
             * Frame loss is least at 20; also works for 949fps @1024x1024px            dwAcqNumBuffers = 20;

            //////////////////////////////////////

            /* Interlaced Camera in Field Mode */
            if (Phx.etParamValue.PHX_CAM_INTERLACED == eCamFormat
               && (Phx.etParamValue.PHX_ACQ_FIELD_12 == eAcqType
                  || Phx.etParamValue.PHX_ACQ_FIELD_21 == eAcqType
                  || Phx.etParamValue.PHX_ACQ_FIELD_NEXT == eAcqType
                  || Phx.etParamValue.PHX_ACQ_FIELD_1 == eAcqType
                  || Phx.etParamValue.PHX_ACQ_FIELD_2 == eAcqType))
            {
                dwBufferHeight /= 2;
            }

            /* Determine PHX_BUS_FORMAT based on the camera format */
            eStat = myPhxCommon.PhxCommonGetBusFormat(eCamSrcCol, dwCamSrcDepth, eCaptureFormat, ref eCaptureFormat);
            if (Phx.etStat.PHX_OK != eStat) goto Error;

            /* Update the PHX_BUS_FORMAT, as it may have changed (above) */
            eStat = Phx.PHX_ParameterSet(hCamera, (Phx.etParam.PHX_BUS_FORMAT | Phx.etParam.PHX_CACHE_FLUSH), ref eCaptureFormat);
            if (Phx.etStat.PHX_OK != eStat) goto Error;

            /* Read back the Buffer Stride */
                      eStat = Phx.PHX_ParameterGet(hCamera, Phx.etParam.PHX_BUF_DST_XLENGTH, ref dwBufferStride);
                      if (Phx.etStat.PHX_OK != eStat) goto Error;

      //      dwBufferStride = 2336;


            /* Init the array of capture buffer handles */
            ahCaptureBuffers = new uint[dwAcqNumBuffers];

            /* Init the array of image buffers */
            asImageBuffers = new Phx.stImageBuff[dwAcqNumBuffers + 1];

            /* Create and initialise our capture buffers (not associated with display) */
            for (int i = 0; i < dwAcqNumBuffers; i++)
            {
                /* We create a capture buffer for our double buffering */
                eStat = Pbl.PBL_BufferCreate(ref ahCaptureBuffers[i], Pbl.etPblBufferMode.PBL_BUFF_SYSTEM_MEM_DIRECT, 0, hCamera, myPhxCommon.PhxCommonDisplayErrorHandler);
                if (Phx.etStat.PHX_OK != eStat) goto Error;

                /* Initialise our capture buffer */
                eStat = Pbl.PBL_BufferParameterSet(ahCaptureBuffers[i], Pbl.etPblParam.PBL_BUFF_WIDTH, ref dwBufferWidth);
                if (Phx.etStat.PHX_OK != eStat) goto Error;
                eStat = Pbl.PBL_BufferParameterSet(ahCaptureBuffers[i], Pbl.etPblParam.PBL_BUFF_HEIGHT, ref dwBufferHeight);
                if (Phx.etStat.PHX_OK != eStat) goto Error;
                eStat = Pbl.PBL_BufferParameterSet(ahCaptureBuffers[i], Pbl.etPblParam.PBL_BUFF_STRIDE, ref dwBufferStride);
                if (Phx.etStat.PHX_OK != eStat) goto Error;
                ePblCaptureFormat = (Pbl.etPblParamValue)eCaptureFormat;
                eStat = Pbl.PBL_BufferParameterSet(ahCaptureBuffers[i], Pbl.etPblParam.PBL_DST_FORMAT, ref ePblCaptureFormat);
                if (Phx.etStat.PHX_OK != eStat) goto Error;
                eStat = Pbl.PBL_BufferInit(ahCaptureBuffers[i]);
                if (Phx.etStat.PHX_OK != eStat) goto Error;

                /* Build up our array of capture buffers */
                Pbl.PBL_BufferParameterGet(ahCaptureBuffers[i], Pbl.etPblParam.PBL_BUFF_ADDRESS, ref asImageBuffers[i].pvAddress);
                asImageBuffers[i].pvContext = (IntPtr)ahCaptureBuffers[i];
            }
            /* Terminate the array */
            asImageBuffers[dwAcqNumBuffers].pvAddress = System.IntPtr.Zero;
            asImageBuffers[dwAcqNumBuffers].pvContext = System.IntPtr.Zero;

            /* The above code has created dwAcqNumBuffers acquisition buffers.
             * Therefore ensure that the Phoenix is configured to use this number, by overwriting
             * the value already loaded from the config file.
             */
            eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_ACQ_NUM_BUFFERS, ref dwAcqNumBuffers);
            if (Phx.etStat.PHX_OK != eStat) goto Error;

            /* These are 'direct' buffers, so we must tell Phoenix about them
             * so that it can capture data directly into them.
             */

            eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_BUF_DST_XLENGTH, ref dwBufferStride);
            if (Phx.etStat.PHX_OK != eStat) goto Error;
            eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_BUF_DST_YLENGTH, ref dwBufferHeight);
            if (Phx.etStat.PHX_OK != eStat) goto Error;
            eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_DST_PTRS_VIRT, asImageBuffers);
            if (Phx.etStat.PHX_OK != eStat) goto Error;
            eParamValue = Phx.etParamValue.PHX_DST_PTR_USER_VIRT;
            eStat = Phx.PHX_ParameterSet(hCamera, (Phx.etParam.PHX_DST_PTR_TYPE | Phx.etParam.PHX_CACHE_FLUSH | Phx.etParam.PHX_FORCE_REWRITE), ref eParamValue);
            if (Phx.etStat.PHX_OK != eStat) goto Error;

            /* We create our display with a NULL hWnd, this will automatically create an image window. */
          //  eStat = Pdl.PDL_DisplayCreate(ref hDisplay, IntPtr.Zero, hCamera, myPhxCommon.PhxCommonDisplayErrorHandler);
          //  if (Phx.etStat.PHX_OK != eStat) goto Error;

            /* We create a display buffer (indirect) */
         //   eStat = Pdl.PDL_BufferCreate(ref hDisplayBuffer, hDisplay, Pdl.etPdlBufferMode.PDL_BUFF_SYSTEM_MEM_INDIRECT);
         //   if (Phx.etStat.PHX_OK != eStat) goto Error;

            /* Initialise the display, this associates the display buffer with the display */
         //   eStat = Pdl.PDL_DisplayInit(hDisplay);
         //   if (Phx.etStat.PHX_OK != eStat) goto Error;

            /* Enable FIFO Overflow events */
            eParamValue = Phx.etParamValue.PHX_INTRPT_FIFO_OVERFLOW;
            eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_INTRPT_SET, ref eParamValue);
            if (Phx.etStat.PHX_OK != eStat) goto Error;

            /* Setup our own event context */
            eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_EVENT_CONTEXT, (void*)&sPhxLive);
            if (Phx.etStat.PHX_OK != eStat) goto Error;

            /* Check if camera is CXP */
            eStat = Phx.PHX_ParameterGet(hCamera, Phx.etParam.PHX_BOARD_VARIANT, ref eParamValue);
                     if (Phx.etStat.PHX_OK != eStat) goto Error;
                     if (Phx.etParamValue.PHX_BOARD_FBD_4XCXP6_2PE8 == eParamValue
                        || Phx.etParamValue.PHX_BOARD_FBD_2XCXP6_2PE8 == eParamValue
                        || Phx.etParamValue.PHX_BOARD_FBD_1XCXP6_2PE8 == eParamValue)
                     {
                         fCameraIsCxp = true;
                     }
             
            /* Set the number of images per buffer */
            //     eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_ACQ_IMAGES_PER_BUFFER, ref ImgPerBuff); // variable to set in the initialization block ~ line # 120
            //     Console.WriteLine("Number of images per buffer = {0}\r\n", ImgPerBuff);

            /* Check that camera is discovered (only applies to CXP) */
                   if (fCameraIsCxp)
                   {
                       myPhxCommon.PhxCommonGetCxpDiscoveryStatus(hCamera, 10, ref fIsCxpCameraDiscovered);
                       if (!fIsCxpCameraDiscovered)
                       {
                           goto Error;
                       }
                   }

            /* To set the capture mode - Continuous or Snapshot
            eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_ACQ_CONTINUOUS, ref CaptureMode); // variable to set in the initialization block ~ line # 120
            if (CaptureMode == 1)
                Console.WriteLine("\r\nCapture Mode is CONTINUOUS\r\n");
            if (CaptureMode == 0)
                Console.WriteLine("\r\nCapture Mode is SNAPSHOT\r\n");
                */

            /* init for reg writing tests */
            //    uint dwValue = 0;
            //    uint ExpTim = 0;
            //    uint CXPspeed = 0;

            //  eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_ROI_XLENGTH, ref ROIx);
            //  eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_ROI_SRC_XOFFSET, ref OffsetX);
            //  eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_ROI_YLENGTH, ref ROIy);
            //  eStat = Phx.PHX_ParameterSet(hCamera, Phx.etParam.PHX_ROI_SRC_YOFFSET, ref OffsetY);

            //   uint a = 0;
            //   uint b = 0;
            //   eStat = Phx.PHX_ParameterGet(hCamera, Phx.etParam.PHX_ROI_XLENGTH, ref a);
            //   eStat = Phx.PHX_ParameterGet(hCamera, Phx.etParam.PHX_ROI_YLENGTH, ref b);


            /* Writing to the camera regs. Figuring this out took ages, and able to implement this, a lifetime */
            // eStat = myPhxCommon.PhxCommonWriteCxpReg(hCamera, 16404, 262216, 500); // connection 4 speed 6250;
            //device not ready error; hex value to be written 0x40048; likely written as mid big endian;
            //      eStat = myPhxCommon.PhxCommonWriteCxpReg(hCamera, 0x4014, 262216, 500);
            eStat = myPhxCommon.PhxCommonWriteCxpReg(hCamera, 0x8814, CameraFPS, 500); // fps dec value 34836
            eStat = myPhxCommon.PhxCommonWriteCxpReg(hCamera, 0x8840, CameraExp, 500); // exposure time 34880
            eStat = myPhxCommon.PhxCommonWriteCxpReg(hCamera, 37648, 1, 500); // frame counter stamp
            eStat = myPhxCommon.PhxCommonWriteCxpReg(hCamera, 37652, 1, 500); // camera time stamps

         //   eStat = myPhxCommon.PhxCommonWriteCxpReg(hCamera, 0x8180, 1, 500); //0x8180 ROI selector
         //   eStat = myPhxCommon.PhxCommonWriteCxpReg(hCamera, 0x8184, 1, 500); // RegionMode: bin; 0x8184
         //   eStat = myPhxCommon.PhxCommonWriteCxpReg(hCamera, 0x3000, ROIx, 500); // Width: 128 to max; to be incremented in steps of 64; 0x3000
         //   eStat = myPhxCommon.PhxCommonWriteCxpReg(hCamera, 0x3004, ROIy, 500); // Height: 1 to max; to be incremented in steps of 1; 0x3004 
         //   eStat = myPhxCommon.PhxCommonWriteCxpReg(hCamera, 0x8800, OffsetX, 500); // OffsetXReg: horizontal offset - again incremented by 64 uptosensorWidth; 0x8800
         //   eStat = myPhxCommon.PhxCommonWriteCxpReg(hCamera, 0x8804, OffsetY, 500); // OffsetYReg: vertical offset - 1 to sensorHeight max, stepsize of 1; 0x8804

            
            /* for reg writing tests */
            // eStat = myPhxCommon.PhxCommonReadCxpReg(hCamera, 34836, ref dwValue, 500);
            // eStat = myPhxCommon.PhxCommonReadCxpReg(hCamera, 34880, ref ExpTim, 500);
            // eStat = myPhxCommon.PhxCommonReadCxpReg(hCamera, 0x4014, ref CXPspeed, 500); // connection 4 speed 6250

            /* Now start our capture, using the callback method */
            
            eStat = Phx.PHX_StreamRead(hCamera, Phx.etAcq.PHX_START, PHX_Callback);
            if (Phx.etStat.PHX_OK != eStat) goto Error;

            /* Now start camera */
            if (fCameraIsCxp && 0 != sCameraRegs.dwAcqStartAddress)
            {
                eStat = myPhxCommon.PhxCommonWriteCxpReg(hCamera, sCameraRegs.dwAcqStartAddress, sCameraRegs.dwAcqStartValue, 1);
                if (Phx.etStat.PHX_OK != eStat) goto Error;
            }

            //videowriter init and open a new instance
            int width = ROIxx;
            int height = ROIyy;
            int fps = VideoFPS;
            VideoFileWriter writer = new VideoFileWriter(); // new instance of ffmpeg videowriter
            string videotimestamp = DateTime.Now.ToString("MM.dd.yyyy_HH.mm.ss.fff"); //timestamp for filename
            writer.Open("C:/Users/fdb/Documents/Gokul/Data/Video/2017.08.10/" + videotimestamp + ".avi", width, height, fps, VideoCodec.MPEG4); //H.264

            //Cal fps init
            uint buffcount = 0;
            int frames = 0;
            int prevframes = 0;

            // frame # check init
            uint LastFrame = 0;

            //Other common fps and frame # check init
            uint Realfps = 0;
            int Calfps = 0;
            uint FrameCheck = 0;

            // Should have figured this out earlier; Init these arrays here outside the loop really 
            //reduces the runtime for the processing loop
            Byte[] MBD_image = new byte[ROIx * ROIy]; // Max resolution is 2336x1728px for the MC4082
            byte[,] CXP_image = new byte[ROIxx, ROIyy];

            //start the timers now! poorly named, ofcourse
            timer.Start(); // for runtime
            fpswatch4R.Start(); // for real-time camera fps
            fpswatch4C.Start(); // for calculated system fps

            // Continue processing data until acquisition timeour set in the begining or until the user presses a key in the console window
            // Console.WriteLine("Press a key to exit");
            //  dwLastBuffer = 1000000000;
            uint RealFrameCount = 0;
            while (timer.ElapsedMilliseconds<AcqTime && 0 == myPhxCommon.PhxCommonKbHit())
            {

                /* Wait here until either:
                 * (a) The acquisition timer finishes run.
                 * (b) The user can abort the wait by pressing a key in the console window.
                 * (c) The BufferReady event occurs indicating that the image is complete.
                 * (d) The FIFO overflow event occurs indicating that the image is corrupt.
                 */

             //   Thread.Sleep(1);

                         while (0 == myPhxCommon.PhxCommonKbHit() && !sPhxLive.fBufferReady && !sPhxLive.fFifoOverflow) // Wait for buffer to be filled
                        {
                            // do nothing!
                        }

                   

                   

             //   eStat = Phx.PHX_ParameterGet(hCamera, Phx.etParam.PHX_COUNT_BUFFER_READY_NOW, ref dwCurrentBuffer);

             //   while (dwCurrentBuffer >= dwLastBuffer)
             //   {                
             //      //do nothing
             //   }

              //  dwLastBuffer = dwCurrentBuffer;

           
                if (dwBufferReadyLast != sPhxLive.dwBufferReadyCount)
                {
                    uint dwStaleBufferCount;

                    /* If the processing is too slow to keep up with acquisition,
                     * then there may be more than 1 buffer ready to process.
                     * The application can either be designed to process all buffers
                     * knowing that it will catch up, or as here, throw away all but the
                     * latest
                     */
                    dwStaleBufferCount = sPhxLive.dwBufferReadyCount - dwBufferReadyLast;
                    dwBufferReadyLast += dwStaleBufferCount;


                    /* Throw away all but the last image */
                    if (1 < dwStaleBufferCount)
                    {
                        do
                        {
                            eStat = Phx.PHX_StreamRead(hCamera, Phx.etAcq.PHX_BUFFER_RELEASE, IntPtr.Zero);
                            if (Phx.etStat.PHX_OK != eStat) goto Error;
                            dwStaleBufferCount--;
                        } while (dwStaleBufferCount > 1);
                    }
                }

                sPhxLive.fBufferReady = false;

                // Init a working buffer to pull information from the ping-pong buffer

                Phx.stImageBuff stBuffer;
                stBuffer.pvAddress = IntPtr.Zero;
                stBuffer.pvContext = IntPtr.Zero;

                GetLast:
                {
                    /* Get the info for the last acquired buffer */
                    eStat = Phx.PHX_StreamRead(hCamera, Phx.etAcq.PHX_BUFFER_GET, ref stBuffer);
                    if (Phx.etStat.PHX_OK != eStat)
                    {
                        eStat = Phx.PHX_StreamRead(hCamera, Phx.etAcq.PHX_BUFFER_RELEASE, IntPtr.Zero);
                        if (Phx.etStat.PHX_OK != eStat) goto Error;
                        continue;

                    }

                    /*  copy data from unmanaged memory buffer to a managed array; this was another early beast*/
                    Marshal.Copy(stBuffer.pvAddress, MBD_image, 0, ROIxx * ROIyy);
                    /* Copying the data into a 2D array*/
                    Buffer.BlockCopy(MBD_image, 0, CXP_image, 0, ROIxx * ROIyy);

                    /* ID frame number */
                    byte[] RealFrameStamp = new byte[4];
                    System.Buffer.BlockCopy(CXP_image, 0, RealFrameStamp, 0, 3);
                    RealFrameCount = System.BitConverter.ToUInt32(RealFrameStamp, 0);
                    //   Console.WriteLine("{0}", RealFrameCount);

                }
                /*CheckBox frame number of the next image with the previous and decide whether to process it 
                 * or throw it (Release -- goes to buffer release) */
                                 
                    //  if (RealFrameCount <= FrameCheck)
                   //   {
                   //       goto GetLast;
                   //   }   



               /* Frame rate from camera information */

                /*   RealFrameStamp = new byte[4];
                     System.Buffer.BlockCopy(CXP_image, 0, RealFrameStamp, 0, 2);
                     RealFrameCount = System.BitConverter.ToUInt32(RealFrameStamp, 0); */
                    if ((int)fpswatch4R.ElapsedMilliseconds >= 1000)
                    {
                        Realfps = (RealFrameCount - LastFrame);
                        //  Console.WriteLine("fps = {0}", Realfps);
                        long fpswatchValue = fpswatch4R.ElapsedMilliseconds;
                        LastFrame = RealFrameCount;
                        fpswatch4R.Reset();
                        fpswatch4R.Start();
                    }

                    FrameCheck = RealFrameCount; // assign the current number to FrameCheck before leaving

                    /* Camera TimseStamp */
                    byte[] RealTimeStamp = new byte[4];
                    System.Buffer.BlockCopy(CXP_image, 4, RealTimeStamp, 0, 4);
                    // Array.Reverse(RealTimeStamp);
                    uint RealTimeCounter = ((System.BitConverter.ToUInt32(RealTimeStamp, 0)) * 40) / 1000000; // based on time counter; 25MHz/40ns
                    // Console.WriteLine("Time = {0}", RealTimeCounter);

                   /* Calculated frame rate */
                    byte[] inFrameCount = new byte[4];
                    System.Buffer.BlockCopy(CXP_image, 10, inFrameCount, 0, 4);
                    uint framecount = System.BitConverter.ToUInt32(inFrameCount, 0);

                    if (buffcount != framecount)
                    {
                        frames++;
                        buffcount = framecount;
                        //     string frametimestamp = DateTime.Now.ToString("MM.dd.yyyy_HH.mm.ss.fffff");
                        //     Console.WriteLine("framecount and time resp = {0} and {1}:", frames, frametimestamp);

                        if ((int)fpswatch4C.ElapsedMilliseconds >= 1000)
                        {
                            //    uint frames = 0;
                            Calfps = (frames - prevframes);
                         //   Console.WriteLine("{0}", Calfps);
                            long fpswatchValue = fpswatch4C.ElapsedMilliseconds;
                            prevframes = frames;
                            fpswatch4C.Reset();
                            fpswatch4C.Start();
                        }

                    }

                    

                /* Parsing bytes to create image and then save */
                     Image <Gray, byte> depthImage = new Image<Gray, byte>(ROIxx, ROIyy); // again only int arguments allowed
                     depthImage.Bytes = MBD_image;

                     depthImage.ROI = new Rectangle(32, 132, 25, 25); // select 625 pixels around the LED
                     var LED_Region = depthImage.GetAverage(depthImage); // calculate average intensity of these LED pixels
                     var LED_intensity = LED_Region.Intensity;

                           if (LED_intensity > 200) // report when LED is ON or OFF
                           { 

                               LEDStatus = 1;
                           }
                           else
                           LEDStatus = 0;

                depthImage.Bytes = MBD_image; // deselecting the ROI

                    //  string imagetimestamp = DateTime.Now.ToString("MM.dd.yyyy_HH.mm.ss.fff");
                   //   depthImage.Save("C:/Users/fdb/Documents/Gokul/Data/Images/" + imagetimestamp + ".tiff");

                /* Write images into a video. This is using AForge ffmpeg assembly  */
                     Bitmap myImage = depthImage.ToBitmap(); //convert to a bitmap

      //                writer.WriteVideoFrame(myImage);
      //                myImage.Dispose();

                /* Print acquisition parametrs to console window and write to disk in csv format */
                string SystemStamp = DateTime.Now.ToString("ss.fff");
                Console.WriteLine("frame # = {0}, Realfps = {1}, Calfps = {2}, CamTim = {3}, SysTim = {4}, LED={5}", RealFrameCount, Realfps, Calfps, RealTimeCounter, SystemStamp, LEDStatus);

                var csvTime = RealTimeCounter.ToString();
                var csvFrame = RealFrameCount.ToString();
                var csvFps = Realfps.ToString();
                var csvCalfps = Calfps.ToString();
                var csvROIx = ROIx.ToString();
                var csvROIy = ROIy.ToString();
                var csvLEDraw = LED_intensity.ToString();
                var csvLED = LEDStatus.ToString();

                var newLine = string.Format("{0},{1},{2},{3},{4},{5},{6}{7}", csvTime, csvFrame, csvFps, csvCalfps, SystemStamp, csvLED, ROIx, ROIy);
                csv.AppendLine(newLine);

                /*  uint hBufferHandle = (uint)stBuffer.pvContext;

                  // UNCOMMENT WHEN REQUIRED TO SEE LIVE IMAGES : 
                  // Also UNCOMMENT display buffer creation in the init block
                  // This copies/converts data from the direct capture buffer to the indirect display buffer

                  eStat = Pil.PIL_Convert(hBufferHandle, hDisplayBuffer);
                  if (Phx.etStat.PHX_OK != eStat)
                  {
                      eStat = Phx.PHX_StreamRead(hCamera, Phx.etAcq.PHX_BUFFER_RELEASE, IntPtr.Zero);
                      if (Phx.etStat.PHX_OK != eStat) goto Error;
                      continue;
                  }

                      Pdl.PDL_BufferPaint(hDisplayBuffer);
                */

                Release:
                /* Having processed the data, release the buffer ready for further image data */
                eStat = Phx.PHX_StreamRead(hCamera, Phx.etAcq.PHX_BUFFER_RELEASE, IntPtr.Zero);
                if (Phx.etStat.PHX_OK != eStat) goto Error;                
            }


            /* In this simple example we abort the processing loop on an error condition (FIFO overflow).
             * However handling of this condition is application specific, and generally would involve
             * aborting the current acquisition, and then restarting.
             */
            if (sPhxLive.fFifoOverflow)
            {
                Console.WriteLine("FIFO Overflow detected. Aborting.");
            }

            string filetimestamp = DateTime.Now.ToString("MM.dd.yyyy_HH.mm.ss.fff"); // for csvfile
            // FileStream filestream = new FileStream("C:/Params/" + filetimestamp + ".txt", FileMode.Create);
            File.WriteAllText("C:/Users/fdb/Documents/Gokul/Data/AcqParams/" + filetimestamp + ".dat", csv.ToString());
            writer.Close(); // closing the video writer

            Error:
            Console.WriteLine("Stoping Acquisition... Merci !");   
                if (fIsCxpCameraDiscovered && 0 != sCameraRegs.dwAcqStopAddress)
                {
                    myPhxCommon.PhxCommonWriteCxpReg(hCamera, sCameraRegs.dwAcqStopAddress, sCameraRegs.dwAcqStopValue, 800);
                }
                Phx.PHX_StreamRead(hCamera, Phx.etAcq.PHX_ABORT, IntPtr.Zero);
               // eStat = myPhxCommon.PhxCommonWriteCxpReg(hCamera, 33288, 1, 500);
            return 0; 
            
        }

        unsafe static int Main(string[] args)
        {
            PhxCommon myPhxCommon = new PhxCommon();
            PhxCommon.tCxpRegisters sCameraRegs = new PhxCommon.tCxpRegisters();
            PhxCommon.tPhxCmd sPhxCmd = new PhxCommon.tPhxCmd();
            int nStatus = 0;

            myPhxCommon.PhxCommonParseCmd(args, ref sPhxCmd);
            myPhxCommon.PhxCommonParseCxpRegs(sPhxCmd.strConfigFileName, ref sCameraRegs);
            nStatus = phxlive(sPhxCmd.eBoardNumber, sPhxCmd.eChannelNumber, sPhxCmd.strConfigFileName, sCameraRegs);
            return nStatus;
        }

    }

}
