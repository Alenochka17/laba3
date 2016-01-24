/*----------------------------------------------------------------------+
 |	filename:	RSRaid.cs                                               |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   02/03/2007	15:40                                       |
 |	author:		Дробанов Артём Федорович (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	Отказоустойчивое кодирование данных                     |
 +----------------------------------------------------------------------+
 |  Реализация функциональности избыточного кодирования данных на       |
 |  уровне входных и выходных векторов                                  |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace RecoveryStar
{
    /// <summary>
    /// Делегат обновления процесса формирования матрицы "F"
    /// </summary>
    /// <param name="progress">Текущий прогресс</param>
    public delegate void OnUpdateRSMatrixFormingProgressHandler(double progress);

    /// <summary>
    /// Делегат завершения процесса формирования матрицы "F"
    /// </summary>
    public delegate void OnRSMatrixFormingFinishHandler();

    /// <summary>
    /// Класс поля Галуа
    /// </summary>
    public class GF16
    {
        #region Construction & Destruction

        public GF16()
        {
            // Инициализируем таблицы "логарифмирования" и "потенцирования"
            GFInit();
        }
        
        #endregion Construction & Destruction

        #region Public Operations

        /// <summary>
        /// Сложение элементов поля Галуа
        /// </summary>
        public int Add(int a, int b)
        {
            return a ^ b;
        }

        /// <summary>
        /// Вычитание элементов поля Галуа
        /// </summary>
        public int Sub(int a, int b)
        {
            return a ^ b;
        }

        /// <summary>
        /// Умножение элементов поля Галуа
        /// </summary>
        public int Mul(int a, int b)
        {
            // Если любой из аргументов равен нулю, результат также - ноль
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
        /// Деление элементов поля Галуа
        /// </summary>
        public int Div(int a, int b)
        {            
            // Если первый аргумент равен нулю, результат также ноль
            if (a == 0)
            {
                return 0;
            }

            // На ноль делить нельзя!
            if (b == 0)
            {
                return -1;
            }
          
            // Выражение "+_GFSize" гарантирует неотрицательное значение индекса
            return _GFExp[_GFLog[a] - _GFLog[b] + _GFSize];           
        }

        /// <summary>
        /// Возведение в степень элемента поля Галуа
        /// </summary>
        public int Pow(int a, int p)
        {
            // Если показатель степени равен "0", то результат - "1"
            if (p == 0)
            {
                return 1;
            }

            // Если основание степени равно "0", то результат - "0"
            if (a == 0)
            {
                return 0;
            }
           
            // Степень числа может быть представлена как произведение
            // логарифма основания и показателя степени (с последующим потенцированием)
            int pow = _GFLog[a] * p;

            // Приводим результат к размерам поля (старшие байты складываем с младшими)
            // и возвращаем значение экспоненты
            return _GFExp[((pow >> _GFPower) & _GFSize) + (pow & _GFSize)];
        }

        /// <summary>
        /// Вычисление обратного элемента поля Галуа
        /// </summary>
        public int Inv(int a)
        {
            return _GFExp[_GFSize - _GFLog[a]];
        }
  
        #endregion Public Operations

        #region Private Operations

        /// <summary>
        /// Инициализация таблиц "логарифмирования" и "потенцирования"
        /// </summary>
        private void GFInit()
        {
            // Таблица "логарифмирования"
            _GFLog = new int[(_GFSize + 1) * 1];
            
            // Таблица "потенцирования"
            _GFExp = new int[(_GFSize + 1) * 2];

            // Заполняем таблицы логарифмирования и потенцирования
            for (int log = 0, b = 1; log < _GFSize; log++)
            {
                _GFLog[b] = log;
                _GFExp[log] = b;
                _GFExp[log + _GFSize] = b; // Дополнительная часть таблицы позволяет
                                           // избежать приведения к размеру поля после
                                           // суммирования результатов логарифмирования

                // Удваиваем значение элемента поля, для которого строятся таблицы
                b <<= 1;

                // Если вышли за размеры поля "GF16", приводим значение к нему
                if (b > _GFSize)
                {
                    b ^= _RSPrimPoly;
                }               
            }
        }

        #endregion Private Operations

        #region Constants

        // Несократимый порождающий полином GF(16)
        private const int _RSPrimPoly = 0x1100B;
        
        // Степень поля Галуа
        private const int _GFPower = 16;

        // Размер поля Галуа
        private const int _GFSize = ((1 << _GFPower) - 1);

        #endregion Constants

        #region Data

        // Таблица "логарифмирования"
        private int[] _GFLog;

        // Таблица "потенцирования"
        private int[] _GFExp;
        
        #endregion Data

    }; // Class GF16

    /// <summary>
    /// Класс базовой части RAID-подобного кодера Рида-Соломона
    /// </summary>
    public abstract class RSRaidBase
    {
        #region Delegates

        /// <summary>
        /// Делегат обновления процесса формирования матрицы "F"
        /// </summary>        
        public OnUpdateRSMatrixFormingProgressHandler OnUpdateRSMatrixFormingProgress;

        /// <summary>
        /// Делегат завершения процесса формирования матрицы "F"
        /// </summary>
        public OnRSMatrixFormingFinishHandler OnRSMatrixFormingFinish;

        #endregion Delegates

        #region Construction & Destruction

        /// <summary>
        /// Конструктор базового класса сущности "RAID-подобный кодек Рида-Соломона"
        /// </summary>
        public RSRaidBase()
        {            
            // Экземляр класса полностью закончил обработку?
            _finished = true;

            // Основная конфигурация сменилась?
            _mainConfigChanged = true;

            // Экземпляр класса инициализирован корректно (пригоден к работе)?
            _configIsOK = false;

            // По-умолчанию устанавливается фоновый приоритет
            _threadPriority = 0;

            // Инициализируем событие прекращения обработки файла
            _exitEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // Инициализируем cобытие продолжения обработки файла
            _executeEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // Событие, устанавливаемое по завершении обработки
            _finishedEvent = new ManualResetEvent[] { new ManualResetEvent(true) };
        }

        #endregion Construction & Destruction

        #region Public Operations

        /// <summary>
        /// Запуск процесса заполнения матрицы "F" данными
        /// </summary>
        /// <param name="runAsSeparateThread">Запускать в отдельном потоке?</param>
        /// <returns></returns>
        public bool Prepare(bool runAsSeparateThread)
        {
            // Если поток формирования матрицы "F" работает - не позволяем повторный запуск
            if (InProcessing)
            {
                return false;
            }

            // Если конфигурация установлена некорректно - выходим
            if (!_configIsOK)
            {
                return false;
            }

            // Сбрасываем индикатор актуального состояния переменных-членов
            _finished = false;

            // Сбрасываем событие завершения обработки
            _finishedEvent[0].Reset();

            // Указываем, что поток должен выполняться
            _exitEvent[0].Reset();
            _executeEvent[0].Set();

            // Если указано, что не требуется запуск в отдельном потоке,
            // запускаем в данном
            if (!runAsSeparateThread)
            {
                // Заполняем матрицу кодирования
                FillF();

                // Возвращаем результат обработки
                return _configIsOK;
            }

            // Создаем поток формирования матрицы "F"...
            _thrRSMatrixForming = new Thread(new ThreadStart(FillF));

            //...затем даем ему имя...
            _thrRSMatrixForming.Name = "RSRaid.FillF()";

            //...устанавливаем выбранный приоритет задачи...
            _thrRSMatrixForming.Priority = _threadPriority;
            
            //...и запускаем
            _thrRSMatrixForming.Start();
            
            return true;
        }

        /// <summary>
        /// Метод остановки потока
        /// </summary>
        public void Stop()
        {
            // Указываем, что поток обработки больше не должен выполняться
            _executeEvent[0].Set(); // Это необходимо для принудительного снятия с паузы
            _exitEvent[0].Set();
        }

        /// <summary>
        /// Постановка потока обработки на паузу
        /// </summary>
        public void Pause()
        {
            // Ставим обработку на паузу
            _executeEvent[0].Reset();
        }

        /// <summary>
        /// Снятие потока обработки с паузы
        /// </summary>
        public void Continue()
        {
            // Снимаем обработку c паузы
            _executeEvent[0].Set();
        }
                       
        #endregion Public Operations

        #region Protected Operations

        /// <summary>
        /// Вычисление массива rowFactor (для перемножения матриц методом Винограда)        
        /// </summary>
        protected void RowFactorCalc()
        {
            // Определяем количество строк в матрице "F"
            int FRows = _F.GetUpperBound(0) + 1;

            // Выделяем память
            _rowFactor = new int[FRows];

            // Вычисляем распределение процентов итераций по стадиям для
            // корректной обработки процентов                        
            Int64 allStageIter    = (_iterOfFirstStage + _iterOfSecondStage + _iterOfThirdStage);
            int percOfFirstStage  = (int)((100 * _iterOfFirstStage)  / allStageIter);
            int percOfSecondStage = (int)((100 * _iterOfSecondStage) / allStageIter);
            int percOfThirdStage  = (int)((100 * _iterOfThirdStage)  / allStageIter);

            // Данная стадия должна занимать хотя бы один процент
            // (для корректности расчетов)
            if (percOfThirdStage == 0)
            {
                percOfThirdStage = 1;
            }

            // Вычисляем значение модуля, который позволит выводить процент обработки
            // ровно при единичном приращении для цикла по "i"
            int progressMod1 = (FRows / percOfThirdStage);

            // Если модуль равен нулю, то увеличиваем его до значения "1", чтобы
            // прогресс выводился на каждой итерации
            if (progressMod1 == 0)
            {
                progressMod1 = 1;
            }

            int d = (_n / 2);

            // Работаем по всем строкам
            for (int i = 0; i < FRows; i++)
            {
                _rowFactor[i] = _eGF16.Mul(_F[i, 0], _F[i, 1]);

                for (int j = 1; j < d; j++)
                {
                    _rowFactor[i] = _eGF16.Add(_rowFactor[i], _eGF16.Mul(_F[i, (2 * j)], _F[i, (2 * j) + 1]));
                }

                // Если есть подписка на делегата обновления прогресса -...    
                if (
                        ((i % progressMod1) == 0)
                     &&
                        (OnUpdateRSMatrixFormingProgress != null)
                   )
                {
                    //...выводим данные
                    OnUpdateRSMatrixFormingProgress((((double)(i + 1) / (double)FRows) * percOfThirdStage) + percOfFirstStage + percOfSecondStage);
                }

                // В случае, если требуется постановка на паузу, событие "_executeEvent"
                // будет сброшено, и будем на паузе вплоть до его появления
                ManualResetEvent.WaitAll(_executeEvent);

                // Если указано, что требуется выйти из потока - выходим
                if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                {
                    // Указываем, что кодер не сконфигурирован корректно
                    _configIsOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }
            }
        }

        /// <summary>
        /// Метод поиска индекса строки, 
        /// </summary>
        /// <param name="rowNum">Номер строки</param>
        /// <returns>Индекс строки, пригодной для замены</returns>
        protected int FindSwapRow(int rowNum)
        {            
            // Пробегаем по всем имеющимся строкам матрицы
            // в указанном столбце
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
        /// Метод перестановки двух строк местами
        /// </summary>
        /// <param name="rowNum1">Индекс первой строки</param>
        /// <param name="rowNum2">Индекс второй строки</param>
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
        /// Метод получения дисперсной матрицы "_D"
        /// </summary>
        protected void MakeDispersal()
        {            
            // Выделяем память под матрицу "F"
            _D = new int[(_n + _m), _n];

            // Заполняем матрицу данными (формируем матрицу Вандермонда)
            for (int i = 0; i < (_n + _m); i++)
            {
                for (int j = 0; j < _n; j++)
                {
                    _D[i, j] = _eGF16.Pow(i, j);
                }
            }

            // Вычисляем распределение процентов итераций по стадиям для
            // корректной обработки процентов                        
            Int64 allStageIter   = (_iterOfFirstStage + _iterOfSecondStage + _iterOfThirdStage);
            int percOfFirstStage = (int)((100 * _iterOfFirstStage) / allStageIter);

            // Данная стадия должна занимать хотя бы один процент
            // (для корректности расчетов)
            if (percOfFirstStage == 0)
            {
                percOfFirstStage = 1;
            }

            // Вычисляем значение модуля, который позволит выводить процент обработки
            // ровно при единичном приращении для цикла по "i"
            int progressMod1 = (_n / percOfFirstStage);

            // Если модуль равен нулю, то увеличиваем его до значения "1", чтобы
            // прогресс выводился на каждой итерации
            if (progressMod1 == 0)
            {
                progressMod1 = 1;
            }

            // Цикл выбора диагонального элемента
            for (int k = 1; k < _n; k++)
            {
                // Ищем строку, в которой элемент на главной
                // диагонали мог бы быть ненулевым
                int swapIdx = FindSwapRow(k);

                // Если подходящая строка не может быть найдена -
                // это ошибка - ...
                if (swapIdx == -1)
                {
                    //...указываем на ошибку конфигурации
                    _configIsOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }

                // Если была найдена строка, отличная от текущей...                
                if (swapIdx != k)
                {
                    //...меняем строки местами
                    SwapRows(swapIdx, k);                    
                }
                
                // Извлекаем диагональный элемент
                int diagElem = _D[k, k];
               
                // Если диагональный элемент не равен "1", умножаем весь столбец
                // на обратный ему элемент, превращая диагональный в "1"
                if (diagElem != 1)
                {
                    // Вычисляем обратный элемент для "diagElem"
                    int diagElemInv = _eGF16.Inv(diagElem);

                    // Производим требуемую обработку элементов столбца -
                    // умножаем его на элемент, обратный "diagElem"
                    for (int i = 0; i < (_n + _m); i++)
                    {
                        _D[i, k] = _eGF16.Mul(_D[i, k], diagElemInv);
                    }
                }

                // Для всех столбцов...
                for (int j = 0; j < _n; j++)
                {
                    // Извлекаем множитель текущего столбца
                    int colMult = _D[k, j];

                    //...не являющихся столбцами разрешающего элемента...
                    if (
                            (j != k)
                         &&
                            (colMult != 0)
                        )
                    {
                        for (int i = 0; i < (_n + _m); i++)
                        {
                            //...производим замену Cj = Cj - Dk,j * Ck
                            _D[i, j] = _eGF16.Sub(_D[i, j], _eGF16.Mul(colMult, _D[i, k]));
                        }                       
                    }
                }

                // Если есть подписка на делегата обновления прогресса -...    
                if (
                        ((k % progressMod1) == 0)
                     &&
                        (OnUpdateRSMatrixFormingProgress != null)
                   )
                {
                    //...выводим данные
                    OnUpdateRSMatrixFormingProgress(((double)(k + 1) / (double)_n) * percOfFirstStage);
                }

                // В случае, если требуется постановка на паузу, событие "_executeEvent"
                // будет сброшено, и будем на паузе вплоть до его появления
                ManualResetEvent.WaitAll(_executeEvent);

                // Если указано, что требуется выйти из потока - выходим
                if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                {
                    // Указываем, что декодер не сконфигурирован корректно
                    _configIsOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }           
            }           
        }

        /// <summary>
        /// Заполнение матрицы "F" данными
        /// </summary>         
        protected virtual void FillF() { }
                      
        #endregion Protected Operations

        #region Public Properties

        /// <summary>
        /// Булевское свойство "Экземпляр класса занят обработкой?"
        /// </summary>
        /// <returns>Экземпляр класса занят обработкой?</returns>                 
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
        /// Экземпляр класса сконфигурирован корректно?
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
        /// Булевское свойство "Экземпляр класса закончил обработку
        /// (имеет актуальное состояние переменных-членов)?"
        /// </summary>   
        public bool Finished
        {
            get
            {
                // Если класс не занят обработкой - возвращаем значение
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
        /// Количество основных томов
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
        /// Количество томов для восстановления
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
        /// Приоритет процесса
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

                    // Устанавливаем выбранный приоритет процесса
                    _thrRSMatrixForming.Priority = _threadPriority;
                }
            }
        }

        /// <summary>
        /// Событие, устанавливаемое по завершении обработки
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

        // Объект класса работы с элементами поля Галуа
        protected GF16 _eGF16;
                 
        // Матрица RAID-подобного кодера Рида-Соломона
        protected int[,] _F;

        // Дисперсная матрица
        protected int[,] _D;

        // Массив данных метода Винограда для перемножения матриц
        protected int[] _rowFactor;

        // Количество основных томов
        protected int _n;

        // Количество томов для восстановления
        protected int _m;

        // Экземпляр класса инициализирован корректно (пригоден к работе)?
        protected bool _configIsOK;

        // Экземляр класса полностью закончил обработку?
        protected bool _finished;

        // Основная конфигурация сменилась?
        protected bool _mainConfigChanged;

        // Количество итераций первой, второй и третьей стадий подготовки матрицы кодирования
        protected Int64 _iterOfFirstStage;
        protected Int64 _iterOfSecondStage;
        protected Int64 _iterOfThirdStage;
                
        // Поток заполнения матрицы "F" перед выполнением кодирования / декодирования
        protected Thread _thrRSMatrixForming;

        // Приоритет процесса подготовки матрицы кодирования
        protected ThreadPriority _threadPriority;

        // Событие прекращения подготовки матрицы кодирования
        protected ManualResetEvent[] _exitEvent;

        // Событие продолжения подготовки матрицы кодирования
        protected ManualResetEvent[] _executeEvent;

        // Событие, устанавливаемое по завершении обработки
        protected ManualResetEvent[] _finishedEvent;
      
        #endregion Data

    } // class RSRaidBase

    /// <summary>
    /// Класс RAID-подобного кодера Рида-Соломона
    /// </summary>
    public class RSRaidEncoder : RSRaidBase
    {
        #region Construction & Destruction

        /// <summary>
        /// Конструктор кодера по-умолчанию
        /// </summary>
        public RSRaidEncoder()
        {
            // Создаем объект класса работы с элементами поля Галуа
            _eGF16 = new GF16();
        }

        /// <summary>
        /// Конструктор кодера
        /// </summary>
        /// <param name="dataCount">Количество основных томов</param>
        /// <param name="eccCount">Количество томов для восстановления</param>       
        public RSRaidEncoder(int dataCount, int eccCount)
        {
            // Установка конфигурации кодера
            SetConfig(dataCount, eccCount);

            // Создаем объект класса работы с элементами поля Галуа
            _eGF16 = new GF16();
        }

        #endregion Construction & Destruction

        #region Public Operations

        /// <summary>
        /// Установка конфигурации кодера
        /// </summary>
        /// <param name="dataCount">Количество основных томов</param>
        /// <param name="eccCount">Количество томов для восстановления</param>   
        /// <returns>Булевский флаг операции установки конфигурации</returns>
        public bool SetConfig(int dataCount, int eccCount)
        {           
            // Проверяем конфигурацию на корректность...
            if (
                    (eccCount < dataCount)
                 &&
                    (eccCount > 0)
                 &&
                    ((dataCount + eccCount) <= 65535)
                )
            {
                // Если основная конфигурация не изменилась -
                // сообщаем об этом
                if (
                        (dataCount != _n)
                     ||
                        (eccCount != _m)
                    )
                {
                    _mainConfigChanged = true;
                }

                //... и сохраняем её
                _n = Math.Abs(dataCount);
                _m = Math.Abs(eccCount);

                // Также пересчитываем количество итераций всех стадий подготовки
                _iterOfFirstStage  = ((_n * _m) * _n) + (_n * ((_n + _m) + (_n * (_n + _m))));
                _iterOfSecondStage = 0; // В кодере нет инвертирования матрицы
                _iterOfThirdStage  = (_m * (_n / 2));
                                
                _configIsOK = true;

            } else
            {
                //...указываем на ошибку конфигурации
                _configIsOK = false;                
            }
          
            return _configIsOK;
        }

        /// <summary>
        /// Метод умножения матицы "F" на входной вектор
        /// </summary>
        /// <param name="data">Входной вектор данных (исходные данные)</param>
        /// <param name="ecc">Выходной вектор данных (избыточные данные)</param>
        /// <returns>Булевский флаг результатата операции</returns>
        public bool Process(int[] data, ref int[] ecc)
        {
            // Если кодер сконфигурирован некорректно, обработка невозможна!
            if (!_configIsOK)
            {
                return false;
            }

            int d = (_n / 2);

            // Данных метода Винограда для перемножения матриц
            int colFactor = _eGF16.Mul(data[0], data[1]);
                        
            for (int i = 1; i < d; i++)
            {
                colFactor = _eGF16.Add(colFactor, _eGF16.Mul(data[2 * i], data[(2 * i) + 1]));                
            }

            // Вычисление результирующей матрицы
            for (int i = 0; i < _m; i++)
            {
                ecc[i] = _eGF16.Add(_rowFactor[i], colFactor);
                
                for (int j = 0; j < d; j++)
                {
                    ecc[i] = _eGF16.Add(ecc[i], _eGF16.Mul(_eGF16.Add(_F[i, (2 * j)], data[(2 * j) + 1]), _eGF16.Add(_F[i,(2 * j) + 1], data[2 * j])));                
                }
            }

             // Прибавление членов в случае нечетной общей размерности
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
        /// Заполнение матрицы Вандермонда данными
        /// </summary>         
        protected override void FillF()
        {            
            // Если основная конфигурация изменилась...
            if (_mainConfigChanged)
            {
                //...производим заполнение дисперсной матрицы...
                MakeDispersal();
             
                //...и сбрасываем флаг
                _mainConfigChanged = false;
            }

            // Выделяем память под матрицу "F"
            _F = new int[_m, _n];
           
            // Работаем со строками
            for (int i = 0; i < _m; i++)
            {
                // Работаем со столбцами
                for (int j = 0; j < _n; j++)
                {                
                    _F[i, j] = _D[(_n + i), j];
                }
           
                // В случае, если требуется постановка на паузу, событие "_executeEvent"
                // будет сброшено, и будем на паузе вплоть до его появления
                ManualResetEvent.WaitAll(_executeEvent);

                // Если указано, что требуется выйти из потока - выходим
                if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                {
                    // Указываем, что кодер не сконфигурирован корректно
                    _configIsOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }           
            }

            // Вычисление массива "rowFactor" (для перемножения матриц методом Винограда)
            RowFactorCalc();
            
            // Если есть подписка на делегата завершения...
            if (OnRSMatrixFormingFinish != null)
            {
                //...сообщаем, что экземпляр класса готов к работе
                OnRSMatrixFormingFinish();
            }

            // Активируем индикатор актуального состояния переменных-членов
            _finished = true;

            // Устанавливаем событие завершения обработки
            _finishedEvent[0].Set();
        }

        #endregion Private Operations

    } // class RSRaidEncoder

    /// <summary>
    /// Класс RAID-подобного декодера Рида-Соломона
    /// </summary>
    public class RSRaidDecoder : RSRaidBase
    {
        #region Construction & Destruction

        /// <summary>
        /// Конструктор декодера по-умолчанию
        /// </summary>
        public RSRaidDecoder()
        {
            // Создаем объект класса работы с элементами поля Галуа
            _eGF16 = new GF16();
        }

        /// <summary>
        /// Конструктор декодера
        /// </summary>
        /// <param name="dataCount">Количество основных томов</param>
        /// <param name="eccCount">Количество томов для восстановления</param>
        /// <param name="volList">Список порядковых номеров имеющихся томов</param>        
        public RSRaidDecoder(int dataCount, int eccCount, int[] volList)
        {        
            // Установка конфигурации кодера
            SetConfig(dataCount, eccCount, volList);
                     
            // Создаем объект класса работы с элементами поля Галуа
            _eGF16 = new GF16();
        }

        #endregion Construction & Destruction

        #region Public Operations

        /// <summary>
        /// Установка конфигурации декодера
        /// </summary>
        /// <param name="dataCount">Количество основных томов</param>
        /// <param name="eccCount">Количество томов для восстановления</param> 
        /// <param name="volList">Список порядковых номеров имеющихся томов</param>  
        /// <returns>Булевский флаг операции установки конфигурации</returns>
        public bool SetConfig(int dataCount, int eccCount, int[] volList)
        {           
            // Проверяем конфигурацию на корректность...
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
                // Если основная конфигурация не изменилась -
                // сообщаем об этом
                if (
                        (dataCount != _n)
                     ||
                        (eccCount != _m)
                    )
                {
                    _mainConfigChanged = true;
                }

                //... и сохраняем её
                _n = Math.Abs(dataCount);
                _m = Math.Abs(eccCount);

                // Также пересчитываем количество всех итераций подготовки
                _iterOfFirstStage  = ((_n * _m) * _n) + (_n * ((_n + _m) + (_n * (_n + _m))));
                _iterOfSecondStage = (_n * (((_n - 1) * (_n - 1)) + (_n * _n)));
                _iterOfThirdStage  = (_m * (_n / 2));
                
                // Выделяем память под массив булевских признаков "строка матрицы "F" тривиальна?"
                _FRowIsTrivial = new bool[dataCount];

                // Сохраняем список имеющихся томов
                _volList = volList;

                _configIsOK = true;

            } else
            {
                //...указываем на ошибку конфигурации
                _configIsOK = false;
            }
            
            return _configIsOK;
        }

        /// <summary>
        /// Метод умножения матицы "F" на входной вектор
        /// </summary>
        /// <param name="data_ecc">Входной вектор данных (исходные данные и избыточные)</param>
        /// <param name="data">Выходной вектор данных (восстановленные исходные данные)</param>
        /// <returns>Булевский флаг результатата операции</returns>
        public bool Process(int[] data_ecc, ref int[] data)
        {
            // Если кодер сконфигурирован некорректно, обработка невозможна!
            if (!_configIsOK)
            {
                return false;
            }

            int d = (_n / 2);

            // Данных метода Винограда для перемножения матриц
            int colFactor = _eGF16.Mul(data_ecc[0], data_ecc[1]);

            for (int i = 1; i < d; i++)
            {
                colFactor = _eGF16.Add(colFactor, _eGF16.Mul(data_ecc[2 * i], data_ecc[(2 * i) + 1]));                
            }

            // Вычисление результирующей матрицы
            for (int i = 0; i < _n; i++)
            {
                data[i] = _eGF16.Add(_rowFactor[i], colFactor);
                
                for (int j = 0; j < d; j++)
                {
                    data[i] = _eGF16.Add(data[i], _eGF16.Mul(_eGF16.Add(_F[i, (2 * j)], data_ecc[(2 * j) + 1]), _eGF16.Add(_F[i,(2 * j) + 1], data_ecc[2 * j])));                    
                }
            }

            // Прибавление членов в случае нечетной общей размерности
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
        /// Поиск матрицы, обратной "F", методом Жордановых исключений
        /// </summary>
        protected void FInv()
        {            
            // Вычисляем распределение процентов итераций по стадиям для
            // корректной обработки процентов            
            Int64 allStageIter    = (_iterOfFirstStage + _iterOfSecondStage + _iterOfThirdStage);
            int percOfFirstStage  = (int)((100 * _iterOfFirstStage)  / allStageIter);
            int percOfSecondStage = (int)((100 * _iterOfSecondStage) / allStageIter);
            
            // Данная стадия должна занимать хотя бы один процент
            // (для корректности расчетов)
            if (percOfSecondStage == 0)
            {
                percOfSecondStage = 1;
            }
                                  
            // Вычисляем значение модуля, который позволит выводить процент обработки
            // ровно при единичном приращении для цикла по "k"
            int progressMod1 = (_n / percOfSecondStage);

            // Если модуль равен нулю, то увеличиваем его до значения "1", чтобы
            // прогресс выводился на каждой итерации
            if (progressMod1 == 0)
            {
                progressMod1 = 1;
            }
            
            // Цикл выбора разрешающего элемента "pivot"
            for (int k = 0; k < _n; k++)
            {
                // Извлекаем разрешающий элемент
                int pivot = _F[k, k];

                // Если разрешающий элемент равен нулю - матрица не имеет обратной
                if (pivot == 0)
                {
                    //...указываем на ошибку конфигурации
                    _configIsOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }
             
                // После извлечения разрешающего элемента помещаем на его место "1"
                _F[k, k] = 1;

                // Работаем со строками...
                for (int i = 0; i < _n; i++)
                {
                    // Если находимся на строке разрешающего элемента - переходим
                    // на новую итерацию
                    if (i == k)
                    {
                        continue;
                    }

                    // Работаем со столбцами
                    for (int j = 0; j < _n; j++)
                    {
                        // Если находимся на столбце разрешающего элемента - переходим
                        // на новую итерацию...
                        if (j == k)
                        {
                            continue;
                        }

                        //...а иначе производим требуемые действия над матрицей:
                        // "A[i,j] = A[i,j] * pivot + A[i,k] * A[k,j]"
                        _F[i, j] = _eGF16.Add(_eGF16.Mul(_F[i, j], pivot), _eGF16.Mul(_F[i, k], _F[k, j]));
                    }
                }

                // В качестве завершения итерации делим всю матрицу на разрешающий элемент
                for (int i = 0; i < _n; i++)
                {
                    for (int j = 0; j < _n; j++)
                    {
                        _F[i, j] = _eGF16.Div(_F[i, j], pivot);
                    }
                }
                
                // Если есть подписка на делегата обновления прогресса -...    
                if (
                        ((k % progressMod1) == 0)
                     &&
                        (OnUpdateRSMatrixFormingProgress != null)
                   )
                {
                    //...выводим данные
                    OnUpdateRSMatrixFormingProgress((((double)(k + 1) / (double)_n) * percOfSecondStage) + percOfFirstStage);
                }

                // В случае, если требуется постановка на паузу, событие "_executeEvent"
                // будет сброшено, и будем на паузе вплоть до его появления
                ManualResetEvent.WaitAll(_executeEvent);

                // Если указано, что требуется выйти из потока - выходим
                if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                {
                    // Указываем, что декодер не сконфигурирован корректно
                    _configIsOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }           
            }

            // Если есть подписка на делегата завершения...
            if (OnRSMatrixFormingFinish != null)
            {
                //...сообщаем, что экземпляр класса готов к работе
                OnRSMatrixFormingFinish();
            }

            // Активируем индикатор актуального состояния переменных-членов
            _finished = true;

            // Устанавливаем событие завершения обработки
            _finishedEvent[0].Set();
        }

        /// <summary>
        /// Заполнение "сигнальной" матрицы "F" (матрицы декодера) данными
        /// </summary>
        protected override void FillF()
        {          
            // Если длина вектора имеющихся томов меньше количества,
            // требуемого для восстановления...
            if (_volList.Length < _n)
            {
                //...указываем на ошибку конфигурации
                _configIsOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }
                                           
            // Выделяем память под матрицу "F"
            _F = new int[_n, _n];

            // Вектор счетчиков всех томов...
            int[] allVolCount = new int[_n + _m];

            //...и вектор ecc-томов для "затыкания" пробелов, созданных
            // утерянными основными томами
            int[] eccVolToFix = new int[_m];

            // Счетчик количества стертых основных томов
            int dataVolMissCount = _n;
                                 
            // Инициализируем массив счетчиков всех томов
            for (int i = 0; i < (_n + _m); i++)
            {
                allVolCount[i] = 0;
            }
           
            // Проводим анализ состава представленных томов на предмет наличия основных
            for (int i = 0; i < _n; i++)
            {      
                // Вычисляем номер текущего тома
                int currVol = Math.Abs(_volList[i]);

                // Если номер тома соответствует допустимому диапазону
                if (currVol < (_n + _m))
                {
                    allVolCount[currVol]++;

                    // Если текущий том является основным, фиксируем данный факт
                    if (currVol < _n)
                    {
                        dataVolMissCount--;
                    }
                
                } else
                {
                    // Указываем на ошибку конфигурации
                    _configIsOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }                
            }

            // Проверяем счетчики томов на ошибочное дублирование
            for (int i = 0; i < (_n + _m); i++)
            {
                // Если некоторый том был указан более чем один раз...
                if (allVolCount[i] > 1)
                {
                    //...указываем на ошибку конфигурации
                    _configIsOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }
            }

            // Если проверка на непротиворечивость не выявила проблем, начинаем
            // формировать матрицу "F"

            // Если основная конфигурация изменилась...
            if (_mainConfigChanged)
            {
                //...производим заполнение дисперсной матрицы...
                MakeDispersal();

                //...и сбрасываем флаг
                _mainConfigChanged = false;
            }
            
            // Для каждого утерянного основного тома ищем том для восстановления
            for (int i = 0, j = 0; i < dataVolMissCount; i++)
            {
                // Движемся по списку томов до тех пор, пока не найдем том для
                // восстановления для затыкания "дырки" (основные тома имеют номера
                // меньше _n (при нумерации с нуля!))
                while (_volList[j] < _n)
                {
                    j++;
                }

                // Сохраняем номер тома для замены утерянного основного тома
                eccVolToFix[i] = _volList[j];

                j++; // j++ позволяет перейти к последующему поиску
            }
                                             
            // Работаем по строкам матрицы (в идеале, все строки должны заполняться
            // строками с единицей на главной диагонали, что соответствует отсутствию
            // повреждений, но allVolCount укажет, как обстоят дела с наличием томов)
            for (int i = 0, e = 0; i < _n; i++)
            {
                // Индекс строки из дисперсной матрицы, которая будет помещена в матрицу кодирования
                int FRowIdx;

                // Если основной том отсутствует, формируем строку матрицы Вандермонда
                if (allVolCount[i] == 0)
                {                    
                    // Вычисляем номер строки матрицы Вандермонда, которую нужно вставить
                    // на место данной строки формируемой матрицы "F"
                    FRowIdx = eccVolToFix[e++];
                                        
                    // Указываем, что данная строка матрицы "F" не тривиальна
                    _FRowIsTrivial[i] = false;

                } else
                {
                    // Формируем в матрице "F" нулевую строку с единицей на главной диагонали
                    // (соответствует имеющемуся основному тому)
                    FRowIdx = i;

                    // Указываем, что данная строка матрицы "F" тривиальна
                    _FRowIsTrivial[i] = true;
                }

                // Формирование строки в матрице кодирования
                for (int j = 0; j < _n; j++)
                {
                    _F[i, j] = _D[FRowIdx, j];
                }
              
                // В случае, если требуется постановка на паузу, событие "_executeEvent"
                // будет сброшено, и будем на паузе вплоть до его появления
                ManualResetEvent.WaitAll(_executeEvent);

                // Если указано, что требуется выйти из потока - выходим
                if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                {
                    //...указываем на ошибку конфигурации
                    _configIsOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }        
            }        
                       
            // Находим обратную матрицу для "F"
            FInv();                   

            // Вычисление массива rowFactor (для перемножения матриц методом Винограда)
            RowFactorCalc();
        }
          
        #endregion Private Operations

        #region Public Properties

        /// <summary>
        /// Список порядковых номеров имеющихся томов
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

        // Массив булевских признаков "строка матрицы "F" тривиальна?"
        private bool[] _FRowIsTrivial;

        // Список порядковых номеров имеющихся томов (нумерация с нуля)
        private int[] _volList;
        
        #endregion Data

    } // class RSRaidDecoder
}