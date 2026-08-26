namespace SF_CableSpreader
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine.SceneManagement;
    using UnityEngine;

    [ExecuteInEditMode]
    public class SF_CableSpreader : MonoBehaviour
    {

        [Header("Cables between electric poles:")]
        public GameObject ElectricPoleStart;
        public GameObject ElectricPoleEnd;

        [Header("Cables visibility:")]
        public bool Cable1Visibility = true;
        public bool Cable2Visibility = true;
        public bool Cable3Visibility = true;
        public bool Cable4Visibility = true;

        [Header("Cables tension:")]
        public float tensionFactor = 0.65f;
        public bool randomTension = true;

        private float defaultDistance = 20f;
        private float scaleFactor1 = 1f;
        private float scaleFactor2 = 1f;
        private float scaleFactor3 = 1f;
        private float scaleFactor4 = 1f;

        [Header("Base end points")]
        public GameObject StartPointBase;
        public GameObject EndPointBase;

        [Header("Cable 1")]
        public GameObject StartPoint1;
        public GameObject EndPoint1;
        public GameObject CableMesh1;

        [Header("Cable 2")]
        public GameObject StartPoint2;
        public GameObject EndPoint2;
        public GameObject CableMesh2;

        [Header("Cable 3")]
        public GameObject StartPoint3;
        public GameObject EndPoint3;
        public GameObject CableMesh3;

        [Header("Cable 4")]
        public GameObject StartPoint4;
        public GameObject EndPoint4;
        public GameObject CableMesh4;



#if UNITY_EDITOR

        private Vector3 previousStartPosition1;
        private Vector3 previousEndPosition1;

        /*
        private Vector3 previousStartPosition2;
        private Vector3 previousEndPosition2;

        private Vector3 previousStartPosition3;
        private Vector3 previousEndPosition3;

        private Vector3 previousStartPosition4;
        private Vector3 previousEndPosition4;
        */

        private float previoustension = 1f;

        private bool propertiesChanged;
        private void OnEnable()
        {
            EditorApplication.update += UpdateInEditor;
        }


        private void OnDisable()
        {
            EditorApplication.update -= UpdateInEditor;
        }


        // only update in Editor if this value is changed (can be add more values)
        private void UpdateInEditor()
        {
            if (!Application.isPlaying)
            {
                if (StartPoint1.transform.position != previousStartPosition1 ||
                        EndPoint1.transform.position != previousEndPosition1 || tensionFactor != previoustension)
                {
                    RotateAndScaleCableMesh();
                    previousStartPosition1 = StartPoint1.transform.position;
                    previousEndPosition1 = EndPoint1.transform.position;
                    previoustension = tensionFactor;
                }
            }
        }
#endif

        private void Start()
        {
            RotateAndScaleCableMesh();
        }
        
        private void SetEndPointFromElectricPole()
        {
        if (ElectricPoleEnd != null || EndPointBase != null )
            {
            
            return;
            }
        }
        
        private void RotateAndScaleCableMesh()
        {
            if (StartPoint1 == null || EndPoint1 == null || CableMesh1 == null)
                return;

            
            if (ElectricPoleEnd != null && EndPointBase != null )
            {               

            Vector3 offsetEndPole = new Vector3(0f, 6.9f, 0f);
            EndPointBase.transform.position = ElectricPoleEnd.transform.position + ElectricPoleEnd.transform.TransformVector(offsetEndPole);
            EndPointBase.transform.rotation = ElectricPoleEnd.transform.rotation;

            }

            if (ElectricPoleStart != null && StartPointBase != null)
            {

                Vector3 offsetStartPole = new Vector3(0f, 6.9f, 0f);
                StartPointBase.transform.position = ElectricPoleStart.transform.position + ElectricPoleStart.transform.TransformVector(offsetStartPole);
                StartPointBase.transform.rotation = ElectricPoleStart.transform.rotation;

            }


            //set position scale and rotation of CableMesh1

            CableMesh1.SetActive(Cable1Visibility);  // set visibility for cable mesh

            if (Cable1Visibility)
            {
                Vector3 direction1 = EndPoint1.transform.position - StartPoint1.transform.position;
                Quaternion targetRotation1 = Quaternion.LookRotation(direction1);
                CableMesh1.transform.rotation = targetRotation1;

                float distance1 = direction1.magnitude;
                float scaleFactor1 = distance1 / defaultDistance;
                if (randomTension)
                {
                    CableMesh1.transform.localScale = new Vector3(1f, Random.Range(0.5f * tensionFactor, 1.5f * tensionFactor), scaleFactor1) * this.scaleFactor1;
                }
                else
                {
                    CableMesh1.transform.localScale = new Vector3(1f, tensionFactor * 1f, scaleFactor1) * this.scaleFactor1;
                }
            }

            //set position scale and rotation of CableMesh2

            CableMesh2.SetActive(Cable2Visibility);  // set visibility for cable mesh

            if (Cable2Visibility)
            {

                Vector3 direction2 = EndPoint2.transform.position - StartPoint2.transform.position;
                Quaternion targetRotation2 = Quaternion.LookRotation(direction2);
                CableMesh2.transform.rotation = targetRotation2;

                float distance2 = direction2.magnitude;
                float scaleFactor2 = distance2 / defaultDistance;
                if (randomTension)
                {
                    CableMesh2.transform.localScale = new Vector3(1f, Random.Range(0.5f * tensionFactor, 1.5f * tensionFactor), scaleFactor2) * this.scaleFactor2;
                } else
                {
                    CableMesh2.transform.localScale = new Vector3(1f, tensionFactor * 1.2f, scaleFactor2) * this.scaleFactor2;
                }
                
            }

            //set position scale and rotation of CableMesh3

            CableMesh3.SetActive(Cable3Visibility);  // set visibility for cable mesh

            if (Cable3Visibility)
            {

                Vector3 direction3 = EndPoint3.transform.position - StartPoint3.transform.position;
                Quaternion targetRotation3 = Quaternion.LookRotation(direction3);
                CableMesh3.transform.rotation = targetRotation3;

                float distance3 = direction3.magnitude;
                float scaleFactor3 = distance3 / defaultDistance;
                if (randomTension)
                {
                    CableMesh3.transform.localScale = new Vector3(1f, Random.Range(0.5f * tensionFactor, 1.5f * tensionFactor), scaleFactor3) * this.scaleFactor3;
                }
                else
                {
                    CableMesh3.transform.localScale = new Vector3(1f, tensionFactor * 0.9f, scaleFactor3) * this.scaleFactor3;
                }

            }

            //set position scale and rotation of CableMesh4

            CableMesh4.SetActive(Cable4Visibility);  // set visibility for cable mesh

            if (Cable4Visibility)
            {
                Vector3 direction4 = EndPoint4.transform.position - StartPoint4.transform.position;
                Quaternion targetRotation4 = Quaternion.LookRotation(direction4);
                CableMesh4.transform.rotation = targetRotation4;

                float distance4 = direction4.magnitude;
                float scaleFactor4 = distance4 / defaultDistance;
                if (randomTension)
                {
                    CableMesh4.transform.localScale = new Vector3(1f, Random.Range(0.5f * tensionFactor, 1.5f * tensionFactor), scaleFactor4) * this.scaleFactor4;
                }
                else
                {
                    CableMesh4.transform.localScale = new Vector3(1f, tensionFactor * 0.7f, scaleFactor4) * this.scaleFactor4;
                }
            }
            
            
        }
    }
}