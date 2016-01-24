/*----------------------------------------------------------------------+
 |	filename:	RSRaid.cs                                               |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   02/03/2007	15:40                                       |
 |	author:		�������� ���� ��������� (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	���������������� ����������� ������                     |
 +----------------------------------------------------------------------+
 |  ���������� ���������������� ����������� ����������� ������ ��       |
 |  ������ ������� � �������� ��������                                  |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace RecoveryStar
{
    /// <summary>
    /// ������� ���������� �������� ������������ ������� "F"
    /// </summary>
    /// <param name="progress">������� ��������</param>
    public delegate void OnUpdateRSMatrixFormingProgressHandler(double progress);

    /// <summary>
    /// ������� ���������� �������� ������������ ������� "F"
    /// </summary>
    public delegate void OnRSMatrixFormingFinishHandler();

    /// <summary>
    /// ����� ���� �����
    /// </summary>
    public class GF16
    {
        #region Construction & Destruction

        public GF16()
        {
            // �������������� ������� "����������������" � "��������������"
            GFInit();
        }
        
        #endregion Construction & Destruction

        #region Public Operations

        /// <summary>
        /// �������� ��������� ���� �����
        /// </summary>
        public int Add(int a, int b)
        {
            return a ^ b;
        }

        /// <summary>
        /// ��������� ��������� ���� �����
        /// </summary>
        public int Sub(int a, int b)
        {
            return a ^ b;
        }

        /// <summary>
        /// ��������� ��������� ���� �����
        /// </summary>
        public int Mul(int a, int b)
        {
            // ���� ����� �� ���������� ����� ����, ��������� ����� - ����
            if (
                   (a == 0)
                ||
                   (b == 0)
               )
            {
                return 0;

            } else
            {                
                return _GFExp[_GFLog[a] + _GFLog[b]];
            }
                      
        }

        /// <summary>
        /// ������� ��������� ���� �����
        /// </summary>
        public int Div(int a, int b)
        {            
            // ���� ������ �������� ����� ����, ��������� ����� ����
            if (a == 0)
            {
                return 0;
            }

            // �� ���� ������ ������!
            if (b == 0)
            {
                return -1;
            }
          
            // ��������� "+_GFSize" ����������� ��������������� �������� �������
            return _GFExp[_GFLog[a] - _GFLog[b] + _GFSize];           
        }

        /// <summary>
        /// ���������� � ������� �������� ���� �����
        /// </summary>
        public int Pow(int a, int p)
        {
            // ���� ���������� ������� ����� "0", �� ��������� - "1"
            if (p == 0)
            {
                return 1;
            }

            // ���� ��������� ������� ����� "0", �� ��������� - "0"
            if (a == 0)
            {
                return 0;
            }
           
            // ������� ����� ����� ���� ������������ ��� ������������
            // ��������� ��������� � ���������� ������� (� ����������� ���������������)
            int pow = _GFLog[a] * p;

            // �������� ��������� � �������� ���� (������� ����� ���������� � ��������)
            // � ���������� �������� ����������
            return _GFExp[((pow >> _GFPower) & _GFSize) + (pow & _GFSize)];
        }

        /// <summary>
        /// ���������� ��������� �������� ���� �����
        /// </summary>
        public int Inv(int a)
        {
            return _GFExp[_GFSize - _GFLog[a]];
        }
  
        #endregion Public Operations

        #region Private Operations

        /// <summary>
        /// ������������� ������ "����������������" � "��������������"
        /// </summary>
        private void GFInit()
        {
            // ������� "����������������"
            _GFLog = new int[(_GFSize + 1) * 1];
            
            // ������� "��������������"
            _GFExp = new int[(_GFSize + 1) * 2];

            // ��������� ������� ���������������� � ��������������
            for (int log = 0, b = 1; log < _GFSize; log++)
            {
                _GFLog[b] = log;
                _GFExp[log] = b;
                _GFExp[log + _GFSize] = b; // �������������� ����� ������� ���������
                                           // �������� ���������� � ������� ���� �����
                                           // ������������ ����������� ����������������

                // ��������� �������� �������� ����, ��� �������� �������� �������
                b <<= 1;

                // ���� ����� �� ������� ���� "GF16", �������� �������� � ����
                if (b > _GFSize)
                {
                    b ^= _RSPrimPoly;
                }               
            }
        }

        #endregion Private Operations

        #region Constants

        // ������������ ����������� ������� GF(16)
        private const int _RSPrimPoly = 0x1100B;
        
        // ������� ���� �����
        private const int _GFPower = 16;

        // ������ ���� �����
        private const int _GFSize = ((1 << _GFPower) - 1);

        #endregion Constants

        #region Data

        // ������� "����������������"
        private int[] _GFLog;

        // ������� "��������������"
        private int[] _GFExp;
        
        #endregion Data

    }; // Class GF16

    /// <summary>
    /// ����� ������� ����� RAID-��������� ������ ����-��������
    /// </summary>
    public abstract class RSRaidBase
    {
        #region Delegates

        /// <summary>
        /// ������� ���������� �������� ������������ ������� "F"
        /// </summary>        
        public OnUpdateRSMatrixFormingProgressHandler OnUpdateRSMatrixFormingProgress;

        /// <summary>
        /// ������� ���������� �������� ������������ ������� "F"
        /// </summary>
        public OnRSMatrixFormingFinishHandler OnRSMatrixFormingFinish;

        #endregion Delegates

        #region Construction & Destruction

        /// <summary>
        /// ����������� �������� ������ �������� "RAID-�������� ����� ����-��������"
        /// </summary>
        public RSRaidBase()
        {            
            // �������� ������ ��������� �������� ���������?
            _finished = true;

            // �������� ������������ ���������?
            _mainConfigChanged = true;

            // ��������� ������ ��������������� ��������� (�������� � ������)?
            _configIsOK = false;

            // ��-��������� ��������������� ������� ���������
            _threadPriority = 0;

            // �������������� ������� ����������� ��������� �����
            _exitEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // �������������� c������ ����������� ��������� �����
            _executeEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // �������, ��������������� �� ���������� ���������
            _finishedEvent = new ManualResetEvent[] { new ManualResetEvent(true) };
        }

        #endregion Construction & Destruction

        #region Public Operations

        /// <summary>
        /// ������ �������� ���������� ������� "F" �������
        /// </summary>
        /// <param name="runAsSeparateThread">��������� � ��������� ������?</param>
        /// <returns></returns>
        public bool Prepare(bool runAsSeparateThread)
        {
            // ���� ����� ������������ ������� "F" �������� - �� ��������� ��������� ������
            if (InProcessing)
            {
                return false;
            }

            // ���� ������������ ����������� ����������� - �������
            if (!_configIsOK)
            {
                return false;
            }

            // ���������� ��������� ����������� ��������� ����������-������
            _finished = false;

            // ���������� ������� ���������� ���������
            _finishedEvent[0].Reset();

            // ���������, ��� ����� ������ �����������
            _exitEvent[0].Reset();
            _executeEvent[0].Set();

            // ���� �������, ��� �� ��������� ������ � ��������� ������,
            // ��������� � ������
            if (!runAsSeparateThread)
            {
                // ��������� ������� �����������
                FillF();

                // ���������� ��������� ���������
                return _configIsOK;
            }

            // ������� ����� ������������ ������� "F"...
            _thrRSMatrixForming = new Thread(new ThreadStart(FillF));

            //...����� ���� ��� ���...
            _thrRSMatrixForming.Name = "RSRaid.FillF()";

            //...������������� ��������� ��������� ������...
            _thrRSMatrixForming.Priority = _threadPriority;
            
            //...� ���������
            _thrRSMatrixForming.Start();
            
            return true;
        }

        /// <summary>
        /// ����� ��������� ������
        /// </summary>
        public void Stop()
        {
            // ���������, ��� ����� ��������� ������ �� ������ �����������
            _executeEvent[0].Set(); // ��� ���������� ��� ��������������� ������ � �����
            _exitEvent[0].Set();
        }

        /// <summary>
        /// ���������� ������ ��������� �� �����
        /// </summary>
        public void Pause()
        {
            // ������ ��������� �� �����
            _executeEvent[0].Reset();
        }

        /// <summary>
        /// ������ ������ ��������� � �����
        /// </summary>
        public void Continue()
        {
            // ������� ��������� c �����
            _executeEvent[0].Set();
        }
                       
        #endregion Public Operations

        #region Protected Operations

        /// <summary>
        /// ���������� ������� rowFactor (��� ������������ ������ ������� ���������)        
        /// </summary>
        protected void RowFactorCalc()
        {
            // ���������� ���������� ����� � ������� "F"
            int FRows = _F.GetUpperBound(0) + 1;

            // �������� ������
            _rowFactor = new int[FRows];

            // ��������� ������������� ��������� �������� �� ������� ���
            // ���������� ��������� ���������                        
            Int64 allStageIter    = (_iterOfFirstStage + _iterOfSecondStage + _iterOfThirdStage);
            int percOfFirstStage  = (int)((100 * _iterOfFirstStage)  / allStageIter);
            int percOfSecondStage = (int)((100 * _iterOfSecondStage) / allStageIter);
            int percOfThirdStage  = (int)((100 * _iterOfThirdStage)  / allStageIter);

            // ������ ������ ������ �������� ���� �� ���� �������
            // (��� ������������ ��������)
            if (percOfThirdStage == 0)
            {
                percOfThirdStage = 1;
            }

            // ��������� �������� ������, ������� �������� �������� ������� ���������
            // ����� ��� ��������� ���������� ��� ����� �� "i"
            int progressMod1 = (FRows / percOfThirdStage);

            // ���� ������ ����� ����, �� ����������� ��� �� �������� "1", �����
            // �������� ��������� �� ������ ��������
            if (progressMod1 == 0)
            {
                progressMod1 = 1;
            }

            int d = (_n / 2);

            // �������� �� ���� �������
            for (int i = 0; i < FRows; i++)
            {
                _rowFactor[i] = _eGF16.Mul(_F[i, 0], _F[i, 1]);

                for (int j = 1; j < d; j++)
                {
                    _rowFactor[i] = _eGF16.Add(_rowFactor[i], _eGF16.Mul(_F[i, (2 * j)], _F[i, (2 * j) + 1]));
                }

                // ���� ���� �������� �� �������� ���������� ��������� -...    
                if (
                        ((i % progressMod1) == 0)
                     &&
                        (OnUpdateRSMatrixFormingProgress != null)
                   )
                {
                    //...������� ������
                    OnUpdateRSMatrixFormingProgress((((double)(i + 1) / (double)FRows) * percOfThirdStage) + percOfFirstStage + percOfSecondStage);
                }

                // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                ManualResetEvent.WaitAll(_executeEvent);

                // ���� �������, ��� ��������� ����� �� ������ - �������
                if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                {
                    // ���������, ��� ����� �� ��������������� ���������
                    _configIsOK = false;

                    // ���������� ��������� ����������� ��������� ����������-������
                    _finished = true;

                    // ������������� ������� ���������� ���������
                    _finishedEvent[0].Set();

                    return;
                }
            }
        }

        /// <summary>
        /// ����� ������ ������� ������, 
        /// </summary>
        /// <param name="rowNum">����� ������</param>
        /// <returns>������ ������, ��������� ��� ������</returns>
        protected int FindSwapRow(int rowNum)
        {            
            // ��������� �� ���� ��������� ������� �������
            // � ��������� �������
            for (int i = rowNum; i < (_n + _m); i++)
            {
                if (_D[i, rowNum] != 0)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// ����� ������������ ���� ����� �������
        /// </summary>
        /// <param name="rowNum1">������ ������ ������</param>
        /// <param name="rowNum2">������ ������ ������</param>
        protected void SwapRows(int rowNum1, int rowNum2)
        {
            for (int j = 0; j < _n; j++)
            {
                int tmp = _D[rowNum1, j];
                _D[rowNum1, j] = _D[rowNum2, j];
                _D[rowNum2, j] = tmp;                
            }
        }

        /// <summary>
        /// ����� ��������� ���������� ������� "_D"
        /// </summary>
        protected void MakeDispersal()
        {            
            // �������� ������ ��� ������� "F"
            _D = new int[(_n + _m), _n];

            // ��������� ������� ������� (��������� ������� �����������)
            for (int i = 0; i < (_n + _m); i++)
            {
                for (int j = 0; j < _n; j++)
                {
                    _D[i, j] = _eGF16.Pow(i, j);
                }
            }

            // ��������� ������������� ��������� �������� �� ������� ���
            // ���������� ��������� ���������                        
            Int64 allStageIter   = (_iterOfFirstStage + _iterOfSecondStage + _iterOfThirdStage);
            int percOfFirstStage = (int)((100 * _iterOfFirstStage) / allStageIter);

            // ������ ������ ������ �������� ���� �� ���� �������
            // (��� ������������ ��������)
            if (percOfFirstStage == 0)
            {
                percOfFirstStage = 1;
            }

            // ��������� �������� ������, ������� �������� �������� ������� ���������
            // ����� ��� ��������� ���������� ��� ����� �� "i"
            int progressMod1 = (_n / percOfFirstStage);

            // ���� ������ ����� ����, �� ����������� ��� �� �������� "1", �����
            // �������� ��������� �� ������ ��������
            if (progressMod1 == 0)
            {
                progressMod1 = 1;
            }

            // ���� ������ ������������� ��������
            for (int k = 1; k < _n; k++)
            {
                // ���� ������, � ������� ������� �� �������
                // ��������� ��� �� ���� ���������
                int swapIdx = FindSwapRow(k);

                // ���� ���������� ������ �� ����� ���� ������� -
                // ��� ������ - ...
                if (swapIdx == -1)
                {
                    //...��������� �� ������ ������������
                    _configIsOK = false;

                    // ���������� ��������� ����������� ��������� ����������-������
                    _finished = true;

                    // ������������� ������� ���������� ���������
                    _finishedEvent[0].Set();

                    return;
                }

                // ���� ���� ������� ������, �������� �� �������...                
                if (swapIdx != k)
                {
                    //...������ ������ �������
                    SwapRows(swapIdx, k);                    
                }
                
                // ��������� ������������ �������
                int diagElem = _D[k, k];
               
                // ���� ������������ ������� �� ����� "1", �������� ���� �������
                // �� �������� ��� �������, ��������� ������������ � "1"
                if (diagElem != 1)
                {
                    // ��������� �������� ������� ��� "diagElem"
                    int diagElemInv = _eGF16.Inv(diagElem);

                    // ���������� ��������� ��������� ��������� ������� -
                    // �������� ��� �� �������, �������� "diagElem"
                    for (int i = 0; i < (_n + _m); i++)
                    {
                        _D[i, k] = _eGF16.Mul(_D[i, k], diagElemInv);
                    }
                }

                // ��� ���� ��������...
                for (int j = 0; j < _n; j++)
                {
                    // ��������� ��������� �������� �������
                    int colMult = _D[k, j];

                    //...�� ���������� ��������� ������������ ��������...
                    if (
                            (j != k)
                         &&
                            (colMult != 0)
                        )
                    {
                        for (int i = 0; i < (_n + _m); i++)
                        {
                            //...���������� ������ Cj = Cj - Dk,j * Ck
                            _D[i, j] = _eGF16.Sub(_D[i, j], _eGF16.Mul(colMult, _D[i, k]));
                        }                       
                    }
                }

                // ���� ���� �������� �� �������� ���������� ��������� -...    
                if (
                        ((k % progressMod1) == 0)
                     &&
                        (OnUpdateRSMatrixFormingProgress != null)
                   )
                {
                    //...������� ������
                    OnUpdateRSMatrixFormingProgress(((double)(k + 1) / (double)_n) * percOfFirstStage);
                }

                // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                ManualResetEvent.WaitAll(_executeEvent);

                // ���� �������, ��� ��������� ����� �� ������ - �������
                if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                {
                    // ���������, ��� ������� �� ��������������� ���������
                    _configIsOK = false;

                    // ���������� ��������� ����������� ��������� ����������-������
                    _finished = true;

                    // ������������� ������� ���������� ���������
                    _finishedEvent[0].Set();

                    return;
                }           
            }           
        }

        /// <summary>
        /// ���������� ������� "F" �������
        /// </summary>         
        protected virtual void FillF() { }
                      
        #endregion Protected Operations

        #region Public Properties

        /// <summary>
        /// ��������� �������� "��������� ������ ����� ����������?"
        /// </summary>
        /// <returns>��������� ������ ����� ����������?</returns>                 
        public bool InProcessing
        {
            get
            {
                if (
                        (_thrRSMatrixForming != null)
                     &&
                        (
                                (_thrRSMatrixForming.ThreadState == ThreadState.Running)
                            ||
                                (_thrRSMatrixForming.ThreadState == ThreadState.WaitSleepJoin)
                        )
                    )
                {
                    return true;

                } else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// ��������� ������ ��������������� ���������?
        /// </summary>        
        public bool ConfigIsOK
        {
            get
            {
                if (!InProcessing)
                {
                    return _configIsOK;

                } else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// ��������� �������� "��������� ������ �������� ���������
        /// (����� ���������� ��������� ����������-������)?"
        /// </summary>   
        public bool Finished
        {
            get
            {
                // ���� ����� �� ����� ���������� - ���������� ��������
                if (!InProcessing)
                {
                    return _finished;

                } else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// ���������� �������� �����
        /// </summary>
        public int DataCount
        {
            get
            {
                if (!InProcessing)
                {
                    return _n;

                } else
                {
                    return -1;
                }
            }
        }

        /// <summary>
        /// ���������� ����� ��� ��������������
        /// </summary>
        public int EccCount
        {
            get
            {
                if (!InProcessing)
                {
                    return _m;

                } else
                {
                    return -1;
                }
            }
        }

        /// <summary>
        /// ��������� ��������
        /// </summary>
        public int ThreadPriority
        {
            get
            {
                return (int)_threadPriority;
            }

            set
            {
                if (
                        (_thrRSMatrixForming != null)
                     &&
                        (_thrRSMatrixForming.IsAlive)
                    )
                {
                    switch (value)
                    {
                        default:
                        case 0:
                        {
                            _threadPriority = System.Threading.ThreadPriority.Lowest;

                            break;
                        }

                        case 1:
                        {
                            _threadPriority = System.Threading.ThreadPriority.BelowNormal;

                            break;
                        }

                        case 2:
                        {
                            _threadPriority = System.Threading.ThreadPriority.Normal;

                            break;
                        }

                        case 3:
                        {
                            _threadPriority = System.Threading.ThreadPriority.AboveNormal;

                            break;
                        }

                        case 4:
                        {
                            _threadPriority = System.Threading.ThreadPriority.Highest;

                            break;
                        }
                    }

                    // ������������� ��������� ��������� ��������
                    _thrRSMatrixForming.Priority = _threadPriority;
                }
            }
        }

        /// <summary>
        /// �������, ��������������� �� ���������� ���������
        /// </summary>
        public ManualResetEvent[] FinishedEvent
        {
            get
            {
                return _finishedEvent;
            }
        }
              
        #endregion Public Properties

        #region Data

        // ������ ������ ������ � ���������� ���� �����
        protected GF16 _eGF16;
                 
        // ������� RAID-��������� ������ ����-��������
        protected int[,] _F;

        // ���������� �������
        protected int[,] _D;

        // ������ ������ ������ ��������� ��� ������������ ������
        protected int[] _rowFactor;

        // ���������� �������� �����
        protected int _n;

        // ���������� ����� ��� ��������������
        protected int _m;

        // ��������� ������ ��������������� ��������� (�������� � ������)?
        protected bool _configIsOK;

        // �������� ������ ��������� �������� ���������?
        protected bool _finished;

        // �������� ������������ ���������?
        protected bool _mainConfigChanged;

        // ���������� �������� ������, ������ � ������� ������ ���������� ������� �����������
        protected Int64 _iterOfFirstStage;
        protected Int64 _iterOfSecondStage;
        protected Int64 _iterOfThirdStage;
                
        // ����� ���������� ������� "F" ����� ����������� ����������� / �������������
        protected Thread _thrRSMatrixForming;

        // ��������� �������� ���������� ������� �����������
        protected ThreadPriority _threadPriority;

        // ������� ����������� ���������� ������� �����������
        protected ManualResetEvent[] _exitEvent;

        // ������� ����������� ���������� ������� �����������
        protected ManualResetEvent[] _executeEvent;

        // �������, ��������������� �� ���������� ���������
        protected ManualResetEvent[] _finishedEvent;
      
        #endregion Data

    } // class RSRaidBase

    /// <summary>
    /// ����� RAID-��������� ������ ����-��������
    /// </summary>
    public class RSRaidEncoder : RSRaidBase
    {
        #region Construction & Destruction

        /// <summary>
        /// ����������� ������ ��-���������
        /// </summary>
        public RSRaidEncoder()
        {
            // ������� ������ ������ ������ � ���������� ���� �����
            _eGF16 = new GF16();
        }

        /// <summary>
        /// ����������� ������
        /// </summary>
        /// <param name="dataCount">���������� �������� �����</param>
        /// <param name="eccCount">���������� ����� ��� ��������������</param>       
        public RSRaidEncoder(int dataCount, int eccCount)
        {
            // ��������� ������������ ������
            SetConfig(dataCount, eccCount);

            // ������� ������ ������ ������ � ���������� ���� �����
            _eGF16 = new GF16();
        }

        #endregion Construction & Destruction

        #region Public Operations

        /// <summary>
        /// ��������� ������������ ������
        /// </summary>
        /// <param name="dataCount">���������� �������� �����</param>
        /// <param name="eccCount">���������� ����� ��� ��������������</param>   
        /// <returns>��������� ���� �������� ��������� ������������</returns>
        public bool SetConfig(int dataCount, int eccCount)
        {           
            // ��������� ������������ �� ������������...
            if (
                    (eccCount < dataCount)
                 &&
                    (eccCount > 0)
                 &&
                    ((dataCount + eccCount) <= 65535)
                )
            {
                // ���� �������� ������������ �� ���������� -
                // �������� �� ����
                if (
                        (dataCount != _n)
                     ||
                        (eccCount != _m)
                    )
                {
                    _mainConfigChanged = true;
                }

                //... � ��������� �
                _n = Math.Abs(dataCount);
                _m = Math.Abs(eccCount);

                // ����� ������������� ���������� �������� ���� ������ ����������
                _iterOfFirstStage  = ((_n * _m) * _n) + (_n * ((_n + _m) + (_n * (_n + _m))));
                _iterOfSecondStage = 0; // � ������ ��� �������������� �������
                _iterOfThirdStage  = (_m * (_n / 2));
                                
                _configIsOK = true;

            } else
            {
                //...��������� �� ������ ������������
                _configIsOK = false;                
            }
          
            return _configIsOK;
        }

        /// <summary>
        /// ����� ��������� ������ "F" �� ������� ������
        /// </summary>
        /// <param name="data">������� ������ ������ (�������� ������)</param>
        /// <param name="ecc">�������� ������ ������ (���������� ������)</param>
        /// <returns>��������� ���� ������������ ��������</returns>
        public bool Process(int[] data, ref int[] ecc)
        {
            // ���� ����� ��������������� �����������, ��������� ����������!
            if (!_configIsOK)
            {
                return false;
            }

            int d = (_n / 2);

            // ������ ������ ��������� ��� ������������ ������
            int colFactor = _eGF16.Mul(data[0], data[1]);
                        
            for (int i = 1; i < d; i++)
            {
                colFactor = _eGF16.Add(colFactor, _eGF16.Mul(data[2 * i], data[(2 * i) + 1]));                
            }

            // ���������� �������������� �������
            for (int i = 0; i < _m; i++)
            {
                ecc[i] = _eGF16.Add(_rowFactor[i], colFactor);
                
                for (int j = 0; j < d; j++)
                {
                    ecc[i] = _eGF16.Add(ecc[i], _eGF16.Mul(_eGF16.Add(_F[i, (2 * j)], data[(2 * j) + 1]), _eGF16.Add(_F[i,(2 * j) + 1], data[2 * j])));                
                }
            }

             // ����������� ������ � ������ �������� ����� �����������
            if ((_n % 2) != 0)
            {
                for (int i = 0; i < _m; i++)
                {
                    ecc[i] = _eGF16.Add(ecc[i], _eGF16.Mul(_F[i, (_n - 1)], data[_n - 1]));                    
                }
            }
            
            return true;
        }

        #endregion Public Operations

        #region Private Operations

        /// <summary>
        /// ���������� ������� ����������� �������
        /// </summary>         
        protected override void FillF()
        {            
            // ���� �������� ������������ ����������...
            if (_mainConfigChanged)
            {
                //...���������� ���������� ���������� �������...
                MakeDispersal();
             
                //...� ���������� ����
                _mainConfigChanged = false;
            }

            // �������� ������ ��� ������� "F"
            _F = new int[_m, _n];
           
            // �������� �� ��������
            for (int i = 0; i < _m; i++)
            {
                // �������� �� ���������
                for (int j = 0; j < _n; j++)
                {                
                    _F[i, j] = _D[(_n + i), j];
                }
           
                // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                ManualResetEvent.WaitAll(_executeEvent);

                // ���� �������, ��� ��������� ����� �� ������ - �������
                if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                {
                    // ���������, ��� ����� �� ��������������� ���������
                    _configIsOK = false;

                    // ���������� ��������� ����������� ��������� ����������-������
                    _finished = true;

                    // ������������� ������� ���������� ���������
                    _finishedEvent[0].Set();

                    return;
                }           
            }

            // ���������� ������� "rowFactor" (��� ������������ ������ ������� ���������)
            RowFactorCalc();
            
            // ���� ���� �������� �� �������� ����������...
            if (OnRSMatrixFormingFinish != null)
            {
                //...��������, ��� ��������� ������ ����� � ������
                OnRSMatrixFormingFinish();
            }

            // ���������� ��������� ����������� ��������� ����������-������
            _finished = true;

            // ������������� ������� ���������� ���������
            _finishedEvent[0].Set();
        }

        #endregion Private Operations

    } // class RSRaidEncoder

    /// <summary>
    /// ����� RAID-��������� �������� ����-��������
    /// </summary>
    public class RSRaidDecoder : RSRaidBase
    {
        #region Construction & Destruction

        /// <summary>
        /// ����������� �������� ��-���������
        /// </summary>
        public RSRaidDecoder()
        {
            // ������� ������ ������ ������ � ���������� ���� �����
            _eGF16 = new GF16();
        }

        /// <summary>
        /// ����������� ��������
        /// </summary>
        /// <param name="dataCount">���������� �������� �����</param>
        /// <param name="eccCount">���������� ����� ��� ��������������</param>
        /// <param name="volList">������ ���������� ������� ��������� �����</param>        
        public RSRaidDecoder(int dataCount, int eccCount, int[] volList)
        {        
            // ��������� ������������ ������
            SetConfig(dataCount, eccCount, volList);
                     
            // ������� ������ ������ ������ � ���������� ���� �����
            _eGF16 = new GF16();
        }

        #endregion Construction & Destruction

        #region Public Operations

        /// <summary>
        /// ��������� ������������ ��������
        /// </summary>
        /// <param name="dataCount">���������� �������� �����</param>
        /// <param name="eccCount">���������� ����� ��� ��������������</param> 
        /// <param name="volList">������ ���������� ������� ��������� �����</param>  
        /// <returns>��������� ���� �������� ��������� ������������</returns>
        public bool SetConfig(int dataCount, int eccCount, int[] volList)
        {           
            // ��������� ������������ �� ������������...
            if (
                    (eccCount < dataCount)
                 &&
                    (eccCount > 0)
                 &&
                    ((dataCount + eccCount) <= 65535)
                 &&
                    (volList.Length >= dataCount)
                )
            {
                // ���� �������� ������������ �� ���������� -
                // �������� �� ����
                if (
                        (dataCount != _n)
                     ||
                        (eccCount != _m)
                    )
                {
                    _mainConfigChanged = true;
                }

                //... � ��������� �
                _n = Math.Abs(dataCount);
                _m = Math.Abs(eccCount);

                // ����� ������������� ���������� ���� �������� ����������
                _iterOfFirstStage  = ((_n * _m) * _n) + (_n * ((_n + _m) + (_n * (_n + _m))));
                _iterOfSecondStage = (_n * (((_n - 1) * (_n - 1)) + (_n * _n)));
                _iterOfThirdStage  = (_m * (_n / 2));
                
                // �������� ������ ��� ������ ��������� ��������� "������ ������� "F" ����������?"
                _FRowIsTrivial = new bool[dataCount];

                // ��������� ������ ��������� �����
                _volList = volList;

                _configIsOK = true;

            } else
            {
                //...��������� �� ������ ������������
                _configIsOK = false;
            }
            
            return _configIsOK;
        }

        /// <summary>
        /// ����� ��������� ������ "F" �� ������� ������
        /// </summary>
        /// <param name="data_ecc">������� ������ ������ (�������� ������ � ����������)</param>
        /// <param name="data">�������� ������ ������ (��������������� �������� ������)</param>
        /// <returns>��������� ���� ������������ ��������</returns>
        public bool Process(int[] data_ecc, ref int[] data)
        {
            // ���� ����� ��������������� �����������, ��������� ����������!
            if (!_configIsOK)
            {
                return false;
            }

            int d = (_n / 2);

            // ������ ������ ��������� ��� ������������ ������
            int colFactor = _eGF16.Mul(data_ecc[0], data_ecc[1]);

            for (int i = 1; i < d; i++)
            {
                colFactor = _eGF16.Add(colFactor, _eGF16.Mul(data_ecc[2 * i], data_ecc[(2 * i) + 1]));                
            }

            // ���������� �������������� �������
            for (int i = 0; i < _n; i++)
            {
                data[i] = _eGF16.Add(_rowFactor[i], colFactor);
                
                for (int j = 0; j < d; j++)
                {
                    data[i] = _eGF16.Add(data[i], _eGF16.Mul(_eGF16.Add(_F[i, (2 * j)], data_ecc[(2 * j) + 1]), _eGF16.Add(_F[i,(2 * j) + 1], data_ecc[2 * j])));                    
                }
            }

            // ����������� ������ � ������ �������� ����� �����������
            if ((_n % 2) != 0)
            {
                for (int i = 0; i < _n; i++)
                {
                    data[i] = _eGF16.Add(data[i], _eGF16.Mul(_F[i, (_n - 1)], data_ecc[_n - 1]));                    
                }
            }

            return true;
        }

        #endregion Public Operations

        #region Private Operations
                
        /// <summary>
        /// ����� �������, �������� "F", ������� ���������� ����������
        /// </summary>
        protected void FInv()
        {            
            // ��������� ������������� ��������� �������� �� ������� ���
            // ���������� ��������� ���������            
            Int64 allStageIter    = (_iterOfFirstStage + _iterOfSecondStage + _iterOfThirdStage);
            int percOfFirstStage  = (int)((100 * _iterOfFirstStage)  / allStageIter);
            int percOfSecondStage = (int)((100 * _iterOfSecondStage) / allStageIter);
            
            // ������ ������ ������ �������� ���� �� ���� �������
            // (��� ������������ ��������)
            if (percOfSecondStage == 0)
            {
                percOfSecondStage = 1;
            }
                                  
            // ��������� �������� ������, ������� �������� �������� ������� ���������
            // ����� ��� ��������� ���������� ��� ����� �� "k"
            int progressMod1 = (_n / percOfSecondStage);

            // ���� ������ ����� ����, �� ����������� ��� �� �������� "1", �����
            // �������� ��������� �� ������ ��������
            if (progressMod1 == 0)
            {
                progressMod1 = 1;
            }
            
            // ���� ������ ������������ �������� "pivot"
            for (int k = 0; k < _n; k++)
            {
                // ��������� ����������� �������
                int pivot = _F[k, k];

                // ���� ����������� ������� ����� ���� - ������� �� ����� ��������
                if (pivot == 0)
                {
                    //...��������� �� ������ ������������
                    _configIsOK = false;

                    // ���������� ��������� ����������� ��������� ����������-������
                    _finished = true;

                    // ������������� ������� ���������� ���������
                    _finishedEvent[0].Set();

                    return;
                }
             
                // ����� ���������� ������������ �������� �������� �� ��� ����� "1"
                _F[k, k] = 1;

                // �������� �� ��������...
                for (int i = 0; i < _n; i++)
                {
                    // ���� ��������� �� ������ ������������ �������� - ���������
                    // �� ����� ��������
                    if (i == k)
                    {
                        continue;
                    }

                    // �������� �� ���������
                    for (int j = 0; j < _n; j++)
                    {
                        // ���� ��������� �� ������� ������������ �������� - ���������
                        // �� ����� ��������...
                        if (j == k)
                        {
                            continue;
                        }

                        //...� ����� ���������� ��������� �������� ��� ��������:
                        // "A[i,j] = A[i,j] * pivot + A[i,k] * A[k,j]"
                        _F[i, j] = _eGF16.Add(_eGF16.Mul(_F[i, j], pivot), _eGF16.Mul(_F[i, k], _F[k, j]));
                    }
                }

                // � �������� ���������� �������� ����� ��� ������� �� ����������� �������
                for (int i = 0; i < _n; i++)
                {
                    for (int j = 0; j < _n; j++)
                    {
                        _F[i, j] = _eGF16.Div(_F[i, j], pivot);
                    }
                }
                
                // ���� ���� �������� �� �������� ���������� ��������� -...    
                if (
                        ((k % progressMod1) == 0)
                     &&
                        (OnUpdateRSMatrixFormingProgress != null)
                   )
                {
                    //...������� ������
                    OnUpdateRSMatrixFormingProgress((((double)(k + 1) / (double)_n) * percOfSecondStage) + percOfFirstStage);
                }

                // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                ManualResetEvent.WaitAll(_executeEvent);

                // ���� �������, ��� ��������� ����� �� ������ - �������
                if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                {
                    // ���������, ��� ������� �� ��������������� ���������
                    _configIsOK = false;

                    // ���������� ��������� ����������� ��������� ����������-������
                    _finished = true;

                    // ������������� ������� ���������� ���������
                    _finishedEvent[0].Set();

                    return;
                }           
            }

            // ���� ���� �������� �� �������� ����������...
            if (OnRSMatrixFormingFinish != null)
            {
                //...��������, ��� ��������� ������ ����� � ������
                OnRSMatrixFormingFinish();
            }

            // ���������� ��������� ����������� ��������� ����������-������
            _finished = true;

            // ������������� ������� ���������� ���������
            _finishedEvent[0].Set();
        }

        /// <summary>
        /// ���������� "����������" ������� "F" (������� ��������) �������
        /// </summary>
        protected override void FillF()
        {          
            // ���� ����� ������� ��������� ����� ������ ����������,
            // ���������� ��� ��������������...
            if (_volList.Length < _n)
            {
                //...��������� �� ������ ������������
                _configIsOK = false;

                // ���������� ��������� ����������� ��������� ����������-������
                _finished = true;

                // ������������� ������� ���������� ���������
                _finishedEvent[0].Set();

                return;
            }
                                           
            // �������� ������ ��� ������� "F"
            _F = new int[_n, _n];

            // ������ ��������� ���� �����...
            int[] allVolCount = new int[_n + _m];

            //...� ������ ecc-����� ��� "���������" ��������, ���������
            // ���������� ��������� ������
            int[] eccVolToFix = new int[_m];

            // ������� ���������� ������� �������� �����
            int dataVolMissCount = _n;
                                 
            // �������������� ������ ��������� ���� �����
            for (int i = 0; i < (_n + _m); i++)
            {
                allVolCount[i] = 0;
            }
           
            // �������� ������ ������� �������������� ����� �� ������� ������� ��������
            for (int i = 0; i < _n; i++)
            {      
                // ��������� ����� �������� ����
                int currVol = Math.Abs(_volList[i]);

                // ���� ����� ���� ������������� ����������� ���������
                if (currVol < (_n + _m))
                {
                    allVolCount[currVol]++;

                    // ���� ������� ��� �������� ��������, ��������� ������ ����
                    if (currVol < _n)
                    {
                        dataVolMissCount--;
                    }
                
                } else
                {
                    // ��������� �� ������ ������������
                    _configIsOK = false;

                    // ���������� ��������� ����������� ��������� ����������-������
                    _finished = true;

                    // ������������� ������� ���������� ���������
                    _finishedEvent[0].Set();

                    return;
                }                
            }

            // ��������� �������� ����� �� ��������� ������������
            for (int i = 0; i < (_n + _m); i++)
            {
                // ���� ��������� ��� ��� ������ ����� ��� ���� ���...
                if (allVolCount[i] > 1)
                {
                    //...��������� �� ������ ������������
                    _configIsOK = false;

                    // ���������� ��������� ����������� ��������� ����������-������
                    _finished = true;

                    // ������������� ������� ���������� ���������
                    _finishedEvent[0].Set();

                    return;
                }
            }

            // ���� �������� �� ������������������ �� ������� �������, ��������
            // ����������� ������� "F"

            // ���� �������� ������������ ����������...
            if (_mainConfigChanged)
            {
                //...���������� ���������� ���������� �������...
                MakeDispersal();

                //...� ���������� ����
                _mainConfigChanged = false;
            }
            
            // ��� ������� ���������� ��������� ���� ���� ��� ��� ��������������
            for (int i = 0, j = 0; i < dataVolMissCount; i++)
            {
                // �������� �� ������ ����� �� ��� ���, ���� �� ������ ��� ���
                // �������������� ��� ��������� "�����" (�������� ���� ����� ������
                // ������ _n (��� ��������� � ����!))
                while (_volList[j] < _n)
                {
                    j++;
                }

                // ��������� ����� ���� ��� ������ ���������� ��������� ����
                eccVolToFix[i] = _volList[j];

                j++; // j++ ��������� ������� � ������������ ������
            }
                                             
            // �������� �� ������� ������� (� ������, ��� ������ ������ �����������
            // �������� � �������� �� ������� ���������, ��� ������������� ����������
            // �����������, �� allVolCount ������, ��� ������� ���� � �������� �����)
            for (int i = 0, e = 0; i < _n; i++)
            {
                // ������ ������ �� ���������� �������, ������� ����� �������� � ������� �����������
                int FRowIdx;

                // ���� �������� ��� �����������, ��������� ������ ������� �����������
                if (allVolCount[i] == 0)
                {                    
                    // ��������� ����� ������ ������� �����������, ������� ����� ��������
                    // �� ����� ������ ������ ����������� ������� "F"
                    FRowIdx = eccVolToFix[e++];
                                        
                    // ���������, ��� ������ ������ ������� "F" �� ����������
                    _FRowIsTrivial[i] = false;

                } else
                {
                    // ��������� � ������� "F" ������� ������ � �������� �� ������� ���������
                    // (������������� ���������� ��������� ����)
                    FRowIdx = i;

                    // ���������, ��� ������ ������ ������� "F" ����������
                    _FRowIsTrivial[i] = true;
                }

                // ������������ ������ � ������� �����������
                for (int j = 0; j < _n; j++)
                {
                    _F[i, j] = _D[FRowIdx, j];
                }
              
                // � ������, ���� ��������� ���������� �� �����, ������� "_executeEvent"
                // ����� ��������, � ����� �� ����� ������ �� ��� ���������
                ManualResetEvent.WaitAll(_executeEvent);

                // ���� �������, ��� ��������� ����� �� ������ - �������
                if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                {
                    //...��������� �� ������ ������������
                    _configIsOK = false;

                    // ���������� ��������� ����������� ��������� ����������-������
                    _finished = true;

                    // ������������� ������� ���������� ���������
                    _finishedEvent[0].Set();

                    return;
                }        
            }        
                       
            // ������� �������� ������� ��� "F"
            FInv();                   

            // ���������� ������� rowFactor (��� ������������ ������ ������� ���������)
            RowFactorCalc();
        }
          
        #endregion Private Operations

        #region Public Properties

        /// <summary>
        /// ������ ���������� ������� ��������� �����
        /// </summary>  
        public int[] VolList
        {
            get
            {
                if (!InProcessing)
                {
                    return _volList;

                } else
                {
                    return null;
                }
            }
        }
        
        #endregion Public Properties

        #region Data

        // ������ ��������� ��������� "������ ������� "F" ����������?"
        private bool[] _FRowIsTrivial;

        // ������ ���������� ������� ��������� ����� (��������� � ����)
        private int[] _volList;
        
        #endregion Data

    } // class RSRaidDecoder
}