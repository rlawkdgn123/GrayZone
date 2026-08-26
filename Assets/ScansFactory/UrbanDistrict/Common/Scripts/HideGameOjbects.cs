namespace HideGameObjectsScript
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;

    public class HideGameOjbects : MonoBehaviour
    {
        // Start is called before the first frame update

        public GameObject[] objectsToHide;

        public void HideObjects()
        {
            for (int i = 0; i < objectsToHide.Length; i++)
            {
                objectsToHide[i].SetActive(false);
            }
        }
        void Start()
        {

            HideObjects();

        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}
