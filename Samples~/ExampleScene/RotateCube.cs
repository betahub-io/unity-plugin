using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BetaHub
{
    public class RotateCube : MonoBehaviour
    {
        // Start is called before the first frame update
        void Start()
        {
            
        }

        // Update is called once per frame
        void Update()
        {
            // rotates slowly in three axis
            transform.Rotate(new Vector3(15, 30, 45) * Time.deltaTime);
        }
    }
}
