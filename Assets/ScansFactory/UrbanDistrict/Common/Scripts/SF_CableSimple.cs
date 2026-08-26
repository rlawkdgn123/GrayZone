namespace SF_CableSimple
{
    using System.Collections;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine.SceneManagement;
    using UnityEngine;

    [ExecuteInEditMode]
    public class SF_CableSimple : MonoBehaviour
    {


        [Header("Cables tension:")]
        public float tensionFactor = 1f;
        public bool randomTension = true;

        private float defaultDistance = 20f;
        private float scaleFactor1 = 1f;

        [Header("Cables visibility:")]
        public bool Cable1Visibility = true;
        public bool Cable2Visibility = true;


        [Header("Base end points")]
        public GameObject StartPoint;
        public GameObject EndPoint;
        public GameObject CableMesh1;
        public GameObject CableMesh2;


#if UNITY_EDITOR

        private Vector3 previousStartPosition1;
        private Vector3 previousEndPosition1;

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

        private void UpdateInEditor()
        {
            if (!Application.isPlaying)
            {
                if (StartPoint.transform.position != previousStartPosition1 ||
                        EndPoint.transform.position != previousEndPosition1 || tensionFactor != previoustension)
                {
                    RotateAndScaleCableMesh();
                    previousStartPosition1 = StartPoint.transform.position;
                    previousEndPosition1 = EndPoint.transform.position;
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
        if (EndPoint != null )
            {
            
            return;
            }
        }
        
        private void RotateAndScaleCableMesh()
        {
            if (StartPoint == null || EndPoint == null || CableMesh1 == null)
                return;

            //Cable mesh 1
            CableMesh1.SetActive(Cable1Visibility);  // set visibility for cable mesh

            if (Cable1Visibility)
            {

                Vector3 direction1 = EndPoint.transform.position - StartPoint.transform.position;
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

            //Cable mesh 2

            CableMesh2.SetActive(Cable2Visibility);  // set visibility for cable mesh

            if (Cable2Visibility)
            {
                Vector3 direction1 = EndPoint.transform.position - StartPoint.transform.position;
                Quaternion targetRotation1 = Quaternion.LookRotation(direction1);
                CableMesh2.transform.rotation = targetRotation1;

                float distance1 = direction1.magnitude;
                float scaleFactor1 = distance1 / defaultDistance;
                if (randomTension)
                {
                    CableMesh2.transform.localScale = new Vector3(1f, Random.Range(0.5f * tensionFactor, 1.5f * tensionFactor), scaleFactor1) * this.scaleFactor1;
                }
                else
                {
                    CableMesh2.transform.localScale = new Vector3(1f, tensionFactor * 1f, scaleFactor1) * this.scaleFactor1;
                }

            }

        }
    }
}