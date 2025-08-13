using UnityEngine;
namespace MMO.Shared
{
    public struct MoveIntent
    {
        public float horizontal;  // -1..1 strafe
        public float vertical;    // -1..1 forward/back
        public float yaw;         // facing in degrees
        public bool sprint;      // run modifier
        public bool jump;        // TRUE only on the frame Space was pressed (edge)
    }
}
