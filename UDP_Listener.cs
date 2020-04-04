
//////////////Heath Attia 

//This script utilizes .Net sockets lib, creates a UDPClient that sends a message to a connected host(listenEndPoint) and listens for a response. This is used for polling a sensor
//connected to the host ESP
//this app sends messages and listens on a client thread
//when the connection is active, a UI indicatior will be green and red when the connection is not connected
//the radar data is used with Unity AR Foundations lib to get an orientation from a target device and with the radar data, generate a trackable point that can be used for World
//tracking and mapping
//future updates will utilize Seperate threads or Async and BURST with Unity Networking API
//and UI tweaks to control points generated from radar instead of loop



//** last update **
//Added a ARPointCloudEventArgs event to this script for compatibility issues with
//the XR.Subsystem and ARPointCloudManager
//NEED TO SOLVE ISSUE WITH SETTING m_PointsUpdated and generating points and tri
//triggering the rendering 

//**UPDATE 1
//Points are being generated properly, BUT they are parented to the tracked pose of the device, which is a bug from placing the TR_Visualizer
//and a tracked pose driver on the same Gameobject, but the tracked pose should be a child of the parent visualizer and AR Origin
//






using System.Collections;
using System;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using UnityEngine;
using System.Text;
using UnityEngine.UI;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.SpatialTracking; 


namespace UnityEngine.XR.ARFoundation
{
    public class UDP_Listener : ARTrackable<XRPointCloud, UDP_Listener>
    {

        private ARSessionOrigin aRSessionOrigin; 

        /// <summary>
        /// //text output for UI property
        /// </summary>
        [SerializeField]
        Text m_LogText;

        public Text logText
        {
            get { return s_udpText; }
            set
            {
                m_LogText = value;
                s_udpText = value;
            }
        }
        /////////
        ///


        /// <summary>
        /// UI list size for messages
        /// </summary>
        [SerializeField]
        int m_VisibleMessageCount = 20;
        public int visibleMessageCount
        {
            get { return s_VisibleMessageCount; }
            set
            {
                m_VisibleMessageCount = value;
                s_VisibleMessageCount = value;
            }
        }
       

        /// <summary>
        /// ///Udp connection indicator [red/green]
        /// </summary>
        [SerializeField]
        Image m_HasUDPconnection;

        public Image hasUDPconnection
        {
            get { return m_HasUDPconnection; }
            set { m_HasUDPconnection = value; }
        }
        //////////
        ///


        /// <summary>
        /// //native array to store radar generated Vec3 Positions
        /// </summary>
        public Unity.Collections.NativeArray<Vector3> positions
        {
            get
            {
                return GetUndisposable(radarPoints);
            }

        }
        ///
        /////////

        /// <summary>
        /// An array of identifiers for each point in the point cloud.
        /// This array is parallel to <see cref="positions"/> and
        /// <see cref="confidenceValues"/>. Check for existence with
        /// <c>identifiers.IsCreated</c>.
        ///// </summary>
        //public NativeArray<ulong> identifiers
        //{
        //    get
        //    {
        //        return GetUndisposable(m_Data.identifiers);
        //    }
        //}


        ///
        [SerializeField]
        bool isCreated;
        public bool IsCreated
        {
            get { return isCreated; }

        }

        private bool exceptionBool = false;

        //bool for UDP conection indicator
        static bool s_HasUDPconnection;

        //text object 
        public static Text s_udpText;

        //radar data[float] obj
        private float s_radarData;

        //Text UI log list 
        static List<string> s_Log = new List<string>();

        //native array to store points 
        static NativeArray<Vector3> radarPoints;

        //string builder obj
        static StringBuilder udp_StringBuilder = new StringBuilder();

        //count cache
        int m_LastMessageCount;

        //UI limit of list size rendered
        static int s_VisibleMessageCount;

        //message received from UDP
        public string message;

        //message sent to poll sensor
        private string send_message = "dhdhdhdhdhdhdhd";

        //
        private byte[] message_data = null;
        //
        private int pointCloundPoints;
        //
        private Thread listenThread;
        //
        private SocketException ex;
        //
        private int count = 0;
        //
        private int update_counter = 0;
        //
        private int radar_pointDelay = 100;

        bool countStarted = false;

        ////event for trigger when points array is full 
        public event Action<ARPointCloudUpdatedEventArgs> updated;

        bool m_PointsUpdated = false;

        //update bool for position array 
        public static bool update_bool { get; set; }
        
        //
        public GameObject builder; 
        


        //udp client obj
        private UdpClient listenClient;
        //esp IP + Port 
        private IPEndPoint listenEndPoint = new IPEndPoint(IPAddress.Parse("192.168.4.22"), 8889);

        private ARSessionOrigin arOrigin;

        private void Awake()
        {
            s_udpText = m_LogText;

            //s_radarData = m_RadarData;

            s_VisibleMessageCount = m_VisibleMessageCount;

            //store 50 points at a time **THIS WILL NEED TO BE UPDATED WITH A USER DEFINE PARAM FOR NUMBER OF POINTS TO VISUALIZE and use for tracking**
            radarPoints = new NativeArray<Vector3>(radar_pointDelay, Allocator.Persistent);

            arOrigin = FindObjectOfType<ARSessionOrigin>();
        }


        void Start()
        {
            //setup and bind to local port 
            listenClient = new UdpClient(7555);

            //connect to host 
            listenClient.Connect(listenEndPoint);

            //encode message to send
            message_data = Encoding.ASCII.GetBytes(send_message);


            //create listen thread
            listenThread = new Thread(new ThreadStart(SimplestReceiver));

            //run thread
            listenThread.Start();


            //print string to LOG UI 
            Log("Starting UDP Connection ....");

            ////send message to Host 
            listenClient.Send(message_data, 15);


            aRSessionOrigin = FindObjectOfType<ARSessionOrigin>();



        }


        private void Update()
        {

            //Log(aRSessionOrigin.camera.transform.position.ToString());
            
            if (m_PointsUpdated && updated != null)
            {
                Log("Event IF ");

                m_PointsUpdated = false;

                updated(new ARPointCloudUpdatedEventArgs());

                countStarted = true;

                
            }


            //log points
            Log(s_radarData.ToString());
            //calc new point 
            Vector3 pos_data = arOrigin.camera.transform.position + (arOrigin.camera.transform.forward * s_radarData);
            //cache rot
            Quaternion rotation = arOrigin.camera.transform.rotation;
            
            radarPoints[count] = pos_data;


   
            //press and hold
            ////
            if (Input.touchCount == 1)
            {
                Touch touch = Input.GetTouch(0);

                if (touch.phase == TouchPhase.Stationary)
                {
                    Log("touch Hold");
                    Instantiate(builder, pos_data, rotation);

                }

            }
            ///
            ///
            /// 


            /////
            ///
            //Double Tap event to destroy all build objects
            if (Input.touchCount == 2)
            {
                GameObject[] GameObjects = (GameObject.FindGameObjectsWithTag("builder"));

                foreach(GameObject a in GameObjects)
                {

                    Destroy(a);

                }

            }
            /////////////
            ///


                

            
            

            //update_counter += 1;

            //use this for the countStarted FLAG
            //
            //clear counter and array to get new set of points 
            if (update_counter == radar_pointDelay)
            { 

                //radarPoints.Dispose();

                update_counter = 0;

                Log("Clear counter and dispose of array..............................");
            }




            lock (s_Log)
            {

                if (m_LastMessageCount != s_Log.Count)
                {
                    udp_StringBuilder.Clear();


                    var startIndex = Mathf.Max(s_Log.Count - s_VisibleMessageCount, 0);

                    for (int i = startIndex; i < s_Log.Count; ++i)
                    {
                        udp_StringBuilder.Append($"{i:000}> {s_Log[i]}\n");

                    }
                    if (udp_StringBuilder != null)
                    {
                        //set text UI Null reference BUG HERE
                        s_udpText.text = udp_StringBuilder.ToString();
                    }

                }

                m_LastMessageCount = s_Log.Count;
            }


           

            //set indicator color 
            m_HasUDPconnection.color = s_HasUDPconnection ? Color.green : Color.red;
            count += 1;

            //if array gets filled to defined count
            if (count == (radarPoints.Length))
            {
                Log("Count reset");
                //
                m_PointsUpdated = true;
                //reset count
                count = 0;

            }


        }
        
        
        //receiver function 
        private void SimplestReceiver()
        {

            while (true)
            {

                try
                {

                    
                    /////
                    ///update connected status 
                    ///
                    if (listenClient.Client.Connected)
                    {

                        //set UDP indicator to green
                        NotifyHasUdpConection();

                    }

                    else
                    {
                        s_HasUDPconnection = false;
                        Log("No UDP Connection");
                    }
                    ////

                    //Read UDP Data from endpoint
                    Byte[] data = listenClient.Receive(ref listenEndPoint);

                    //char[] udp_msg;
                    string udp_msg;

                    if (data != null)
                    {
                       
                        udp_msg = Encoding.ASCII.GetString(data);

                        
                        //parse message to float if conditions are met 
                        if (float.TryParse(udp_msg.ToString(), out s_radarData))
                        {

                            Log(s_radarData.ToString());

                          
                                // Log("Points Added" + " " + udp_msg[0]);
                                //calc radar point Using current position(world coordinates + )

                                //radarPoints[count] = arOrigin.camera.transform.position + (arOrigin.camera.transform.forward * s_radarData);

                                //Log(radarPoints[count].ToString() + " " + s_radarData + " This is radar data ");

                        }
                        //if Try parse fails 
                        else if (!float.TryParse(message, out s_radarData))
                        {
                            //Log("Position Data Parse error" + " " + udp_msg[0].ToString());

                        }
                    }




                }

                //exception
                catch (SocketException ex)
                {
                    if (ex.ErrorCode != 10060)
                        Debug.Log("a more serious error " + ex.ErrorCode);


                    else
                        Debug.Log("expected timeout error");

                    //set udp indicator to false [red]
                    s_HasUDPconnection = false;

                    exceptionBool = true;


                }

                Thread.Sleep(5); // tune for your situation
            }
        }

        //if udp connection is successful 
        public static void NotifyHasUdpConection()
        {
            s_HasUDPconnection = true;
        }


        // Creates an alias to the same array, but the caller cannot Dispose it.
        unsafe NativeArray<T> GetUndisposable<T>(NativeArray<T> disposable) where T : struct
        {
            if (!disposable.IsCreated)
                return default(NativeArray<T>);

            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(
                disposable.GetUnsafePtr(),
                disposable.Length,
                Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                ref array,
                NativeArrayUnsafeUtility.GetAtomicSafetyHandle(disposable));
#endif

            return array;
        }


        //void OnDestroy() { CleanUp(); }
        //void OnDisable() { CleanUp(); }

        void OnApplicationQuit()
        {
            CleanUp();
        }
        // be certain to catch ALL possibilities of exit in your environment,
        // or else the thread will typically live on beyond the app quitting.

        void CleanUp()
        {
            //dispose of native array 
            radarPoints.Dispose();

            // note, consider carefully that it may not be running
            listenClient.Close();

            //if exception 
            if (exceptionBool)
            {
                //close thread 
                listenThread.Abort();
                listenThread.Join(5000);
                listenThread = null;

            }

            //
            else
            {
                //close thread 
                listenThread.Abort();
                listenThread.Join(5000);
                listenThread = null;

            }



        }

        /// <summary>
        /// ADDED THE UPDATEDfor the RaycastManager***
        /// </summary>
        internal void UpdateData()
        {
            radarPoints.Dispose();

            m_PointsUpdated = radarPoints.IsCreated;
        }


        //log to app console
        //used for string messages for debugging 
        public static void Log(string message)
        {
            lock (s_Log)
            {
                if (s_Log == null)
                    s_Log = new List<string>();

                s_Log.Add(message);
            }
        }
    }
}
