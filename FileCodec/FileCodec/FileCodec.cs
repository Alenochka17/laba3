/*----------------------------------------------------------------------+
 |	filename:	FileCodec.cs                                            |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   01/03/2007	11:30                                       |
 |  author:		Дробанов Артём Федорович (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	Работа с файлами                                        |
 +----------------------------------------------------------------------+
 |  Кодирование множества файлов в RAID-подобной схеме                  |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO;

namespace RecoveryStar
{
    /// <summary>
    /// Делегат обновления прогресса кодирования файлов
    /// </summary>
    /// <param name="progress">Текущий прогресс</param>
    public delegate void OnUpdateFileCodingProgressHandler(double progress);

    /// <summary>
    /// Делегат завершения процесса кодирования файлов
    /// </summary>
    public delegate void OnFileCodingFinishHandler();
    
    /// <summary>
    /// Класс для кодирования файлов в RAID-подобной схеме
    /// </summary>
    public class FileCodec
    {
        #region Construction & Destruction

        public FileCodec()
        {
            // Инициализируем экземпляр класса для упаковки (распаковки) имени файла
            // в префиксный формат
            _eFileNamer = new FileNamer();

            // Путь к файлам для обработки по-умолчанию пустой
            _path = "";

            // Инициализируем имя файла по-умолчанию            
            _filename = "NONAME";

            // Экземляр класса полностью закончил обработку?
            _finished = true;

            // Обработка произведена корректно?
            _processedOK = false;

            // По-умолчанию устанавливается фоновый приоритет
            _threadPriority = 0;

            // Инициализируем событие прекращения обработки файлов
            _exitEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // Инициализируем cобытие продолжения обработки файлов
            _executeEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // Инициализируем cобытие "пробуждения" цикла ожидания
            _wakeUpEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // Событие, устанавливаемое по завершении обработки
            _finishedEvent = new ManualResetEvent[] { new ManualResetEvent(true) };
        }

        #endregion Construction & Destruction

        #region Delegates

        /// <summary>
        /// Делегат обновления процесса формирования матрицы "F"
        /// </summary>        
        public OnUpdateRSMatrixFormingProgressHandler OnUpdateRSMatrixFormingProgress;

        /// <summary>
        /// Делегат завершения процесса формирования матрицы "F"
        /// </summary>
        public OnRSMatrixFormingFinishHandler OnRSMatrixFormingFinish;

        /// <summary>
        /// Делегат обновления прогресса кодирования файлов
        /// </summary>        
        public OnUpdateFileCodingProgressHandler OnUpdateFileCodingProgress;

        /// <summary>
        /// Делегат завершения процесса разбиения (склеивания) файла
        /// </summary>
        public OnFileCodingFinishHandler OnFileCodingFinish;

        #endregion Delegates

        #region Public Operations

        /// <summary>
        /// Вычисление информации для восстановления основных томов
        /// </summary>
        /// <param name="path">Путь к файлам для обработки</param>
        /// <param name="filename">Имя файла, которому принадлежит множество томов</param>        
        /// <param name="dataCount">Количество основных томов</param>
        /// <param name="eccCount">Количество томов для восстановления</param> 
        /// <param name="runAsSeparateThread">Запускать в отдельном потоке?</param>
        /// <returns>Булевский флаг операции</returns>
        public bool StartToEncode(String path, String filename, int dataCount, int eccCount, bool runAsSeparateThread)
        {            
            // Если поток кодирования файла работает - не позволяем повторный запуск        
            if (InProcessing)
            {
                return false;
            }
            
            // Сбрасываем флаг корректности результата перед запуском потока
            _processedOK = false;

            // Сбрасываем индикатор актуального состояния переменных-членов
            _finished = false;
            
            // Сохраняем путь к файлам для обработки
            if (path == null)
            {
                _path = "";

            } else
            {
                // Производим выделение пути из "_path" в случае,
                // если туда было записано полное имя
                _path = _eFileNamer.GetPath(path);
            }

            if (filename == null)
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }
                        
            // Производим выделение короткого имени файла из "_filename" в случае,
            // если туда было записано полное имя
            _filename = _eFileNamer.GetShortFilename(filename);
                                 
            // Проверяем на корректную конфигурацию
            if (
                    (eccCount > dataCount)
                 ||
                    (eccCount <= 0)
                 ||
                    ((dataCount + eccCount) > 65535)
                )
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }
             
            // Сохраняем количество основных томов
            _dataCount = dataCount;

            // Сохраняем количество томов для восстановления
            _eccCount = eccCount;
                       
            // Указываем, что поток должен выполняться
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // Если указано, что не требуется запуск в отдельном потоке,
            // запускаем в данном
            if (!runAsSeparateThread)
            {
                // Кодируем набор томов с получением томов для восстановления
                Encode();

                // Возвращаем результат обработки
                return _processedOK;
            }

            // Создаем поток кодирования файлов...
            _thrFileCodingProc = new Thread(new ThreadStart(Encode));

            //...затем даем ему имя...
            _thrFileCodingProc.Name = "FileCodec.Encode()";

            //...устанавливаем выбранный приоритет задачи...
            _thrFileCodingProc.Priority = _threadPriority;
            
            //...и запускаем его
            _thrFileCodingProc.Start();

            // Сообщаем, что все нормально
            return true;
        }
        
        /// <summary>
        /// Восстановление утерянных основных томов
        /// </summary>
        /// <param name="path">Путь к файлам для обработки</param>
        /// <param name="filename">Имя файла, которому принадлежит множество томов</param>                
        /// <param name="dataCount">Количество основных томов</param>
        /// <param name="eccCount">Количество томов для восстановления</param>        
        /// <param name="volList">Список номеров имеющихся томов</param>        
        /// <param name="runAsSeparateThread">Запускать в отдельном потоке?</param>
        /// <returns>Булевский флаг операции</returns>               
        public bool StartToDecode(String path, String filename, int dataCount, int eccCount, int[] volList, bool runAsSeparateThread)
        {
            // Если поток декодирования файла работает - не позволяем повторный запуск        
            if (InProcessing)
            {
                return false;
            }
            
            // Сбрасываем флаг корректности результата перед запуском потока
            _processedOK = false;

            // Сбрасываем индикатор актуального состояния переменных-членов
            _finished = false;
            
            // Сохраняем путь к файлам для обработки
            if (path == null)
            {
                _path = "";

            } else
            {
                // Производим выделение пути из "_path" в случае,
                // если туда было записано полное имя
                _path = _eFileNamer.GetPath(path);
            }

            if (filename == null)
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }

            // Производим выделение короткого имени файла из "_filename" в случае,
            // если туда был записано полное имя
            _filename = _eFileNamer.GetShortFilename(filename);
            
            // Проверяем на корректную конфигурацию
            if (
                    (eccCount > dataCount)
                 ||
                    (eccCount <= 0)
                 ||
                    ((dataCount + eccCount) > 65535)
                )
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }
            
            // Сохраняем количество основных томов
            _dataCount = dataCount;

            // Сохраняем количество томов для восстановления
            _eccCount = eccCount;

            // Сохраняем список номеров имеющихся томов
            _volList = volList;
            
            // Указываем, что поток должен выполняться
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // Если указано, что не требуется запуск в отдельном потоке,
            // запускаем в данном
            if (!runAsSeparateThread)
            {
                // Декодируем последовательность файлов с восстановлением основных томов
                Decode();

                // Возвращаем результат обработки
                return _processedOK;
            }
       
            // Создаем поток восстановления основных томов...
            _thrFileCodingProc = new Thread(new ThreadStart(Decode));

            //...затем даем ему имя...
            _thrFileCodingProc.Name = "FileCodec.Decode()";

            //...устанавливаем выбранный приоритет задачи...
            _thrFileCodingProc.Priority = _threadPriority;
           
            //...и запускаем его
            _thrFileCodingProc.Start();

            // Сообщаем, что все нормально
            return true;
        }
        
        /// <summary>
        /// Метод остановки потока
        /// </summary>
        public void Stop()
        {
            // Указываем, что поток обработки больше не должен выполняться            
            _executeEvent[0].Set(); // Сначала снимаем режим "Пауза"...
            _wakeUpEvent[0].Set();  //...а, затем, снимаем с "дремоты"...     
            _exitEvent[0].Set();    //...и указываем, что требуется выход
        }

        /// <summary>
        /// Постановка потока обработки на паузу
        /// </summary>
        public void Pause()
        {
            // Ставим обработку на паузу
            _executeEvent[0].Reset(); // Сначала устанавливаем режим "Пауза"...
            _wakeUpEvent[0].Set();    //...а, затем, снимаем с "дремоты"...            
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

        #region Private Operations

        /// <summary>
        /// Кодирование последовательности файлов
        /// </summary>
        private void Encode()
        {
            // Создаем RAID-подобный кодер Рида-Соломона
            if (_eRSRaidEncoder == null)
            {
                _eRSRaidEncoder = new RSRaidEncoder(_dataCount, _eccCount);

            } else
            {
                _eRSRaidEncoder.SetConfig(_dataCount, _eccCount);
            }

            // Подписываемся на делегатов
            _eRSRaidEncoder.OnUpdateRSMatrixFormingProgress = OnUpdateRSMatrixFormingProgress;
            _eRSRaidEncoder.OnRSMatrixFormingFinish = OnRSMatrixFormingFinish;

            // Запускаем подготовку RAID-подобного кодера Рида-Соломона
            if (_eRSRaidEncoder.Prepare(true))
            {
                // Цикл ожидания завершения подготовки кодера Рида-Соломона к работе
                while (true)
                {
                    // Если не обнаружили установленного события "_executeEvent",
                    // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...приостанавливаем работу контролируемого алгоритма...                      
                        _eRSRaidEncoder.Pause();

                        //...и сами засыпаем
                        ManualResetEvent.WaitAll(_executeEvent);
                        
                        // А когда проснулись, указываем, что обработка должна продолжаться                            
                        _eRSRaidEncoder.Continue();
                    }

                    // Ждем любое из перечисленных событий...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eRSRaidEncoder.FinishedEvent[0] });

                    //...если получили сигнал к тому, чтобы проснуться -
                    // переходим на новую итерацию, т.к. просыпаемся
                    // перед постановкой на паузу...
                    if (eventIdx == 0)
                    {
                        //...предварительно сбросив событие, заставившее нас проснуться
                        _wakeUpEvent[0].Reset();

                        continue;
                    }

                    //...если получили сигнал к выходу из обработки...
                    if (eventIdx == 1)
                    {
                        //...останавливаем контролируемый алгоритм
                        _eRSRaidEncoder.Stop();

                        // Указываем на то, что обработка была прервана
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...если получили сигнал о завершении обработки вложенным алгоритмом...
                    if (eventIdx == 2)
                    {
                        //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                        break;
                    }

                } // while(true)

            } else
            {
                // Сбрасываем флаг корректности результата
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // Когда поток уже не работает, установленное им булевское свойство,
            // возможно, ещё "не проявилось". Достаточно необычная ситуация,
            // но факт - вещь упрямая.
            for (int i = 0; i < 9000; i++)
            {
                if (!_eRSRaidEncoder.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // Если кодер не сконфигурировался корректно - выходим...
            if (!_eRSRaidEncoder.ConfigIsOK)
            {
                //...указывая на ошибку
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // Выделяем память под массивы входных и выходных данных кодера
            int[] source = new int[_dataCount];
            int[] target = new int[_eccCount];

            // "Маленький" буфер для работы с парами байт
            byte[] smallBuff = new byte[2];

            // Выделяем память под массивы файловых потоков
            BufferedStream[] fileStreamSourceArr = new BufferedStream[_dataCount];
            BufferedStream[] fileStreamTargetArr = new BufferedStream[_eccCount];
                       
            try
            {               
                // Имя файла для обработки
                String filename;

                // Номер текущего тома
                int volNum;

                // Инициализируем массивы файловых потоков основных томов
                for (volNum = 0; volNum < _dataCount; volNum++)
                {
                    // Считываем первоначальное имя файла,...
                    filename = _filename;

                    //...упаковываем его в префиксный формат...
                    _eFileNamer.Pack(ref filename, volNum, _dataCount, _eccCount);

                    //...формируем полное имя файла...
                    filename = _path + filename;

                    //...и открываем на его основе входной файловый поток
                    fileStreamSourceArr[volNum] = new BufferedStream(new FileStream(filename, FileMode.Open, System.IO.FileAccess.Read));
                }

                // Инициализируем массивы файловых потоков томов для восстановления
                for (int eccNum = 0; volNum < (_dataCount + _eccCount); volNum++, eccNum++)
                {
                    // Считываем первоначальное имя файла...
                    filename = _filename;

                    //...упаковываем его в префиксный формат...
                    _eFileNamer.Pack(ref filename, volNum, _dataCount, _eccCount);

                    //...формируем полное имя файла...
                    filename = _path + filename;

                    //...и открываем на его основе выходной файловый поток
                    fileStreamTargetArr[eccNum] = new BufferedStream(new FileStream(filename, FileMode.Create, System.IO.FileAccess.Write));                   
                }

                // Вычисляем значение модуля, который позволит выводить процент обработки
                // ровно при единичном приращении
                int progressMod1 = (int)(fileStreamSourceArr[0].Length / 200);

                // Если модуль равен нулю, то увеличиваем его до значения "1", чтобы
                // прогресс выводился на каждой итерации (файл очень маленький)
                if (progressMod1 == 0)
                {
                    progressMod1 = 1;
                }
                                                                   
                // Работаем со всеми срезами пар байт в исходных потоках
                for (int i = 0; i < (fileStreamSourceArr[0].Length / 2); i++)
                {
                    // Заполняем вектор исходных данных кодера данными текущего среза
                    for (int j = 0; j < _dataCount; j++)
                    {
                        // Читаем пару байт из входного потока
                        fileStreamSourceArr[j].Read(smallBuff, 0, 2);

                        // Производим слияние двух значений byte в int
                        source[j] = (int)(((uint)(smallBuff[0] << 0) & 0x00FF)
                                           |
                                          ((uint)(smallBuff[1] << 8) & 0xFF00));
                    }

                    // Кодируем данные (получаем тома для восстановления)
                    _eRSRaidEncoder.Process(source, ref target);

                    // Выводим в файлы вектор избыточных данных (ecc)
                    for (int j = 0; j < _eccCount; j++)
                    {            
                        // Производим разделение одного значения на два (int16 на два byte)
                        smallBuff[0] = (byte)((target[j] >> 0) & 0x00FF);
                        smallBuff[1] = (byte)((target[j] >> 8) & 0x00FF);

                        // Теперь пишем пару байт в выходной поток
                        fileStreamTargetArr[j].Write(smallBuff, 0, 2);
                    }

                    // Выводим прогресс обработки через каждый процент
                    if (
                            ((i % progressMod1) == 0)
                         &&
                            (OnUpdateFileCodingProgress != null)
                       )
                    {
                        OnUpdateFileCodingProgress(((double)(i + 1) / (double)fileStreamSourceArr[0].Length) * 200.0);
                    }

                    // В случае, если требуется постановка на паузу, событие "_executeEvent"
                    // будет сброшено, и будем на паузе вплоть до его появления
                    ManualResetEvent.WaitAll(_executeEvent);

                    // Если указано, что требуется выйти из потока - выходим
                    if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                    {
                        // Закрываем входные файловые потоки
                        for (int j = 0; j < _dataCount; j++)
                        {
                            if (fileStreamSourceArr[j] != null)
                            {
                                fileStreamSourceArr[j].Close();
                                fileStreamSourceArr[j] = null;
                            }
                        }

                        // Закрываем выходные файловые потоки
                        for (int j = 0; j < _eccCount; j++)
                        {
                            if (fileStreamTargetArr[j] != null)
                            {
                                fileStreamTargetArr[j].Close();
                                fileStreamTargetArr[j] = null;
                            }
                        }

                        // Указываем на то, что обработка была прервана
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }
                }
                
                // Закрываем входные файловые потоки
                for (int i = 0; i < _dataCount; i++)
                {
                    if (fileStreamSourceArr[i] != null)
                    {
                        fileStreamSourceArr[i].Close();
                        fileStreamSourceArr[i] = null;
                    }
                }

                // Закрываем выходные файловые потоки
                for (int i = 0; i < _eccCount; i++)
                {
                    if (fileStreamTargetArr[i] != null)
                    {
                        fileStreamTargetArr[i].Flush();
                        fileStreamTargetArr[i].Close();
                        fileStreamTargetArr[i] = null;
                    }
                }

                // Сообщаем, что обработка файлов закончена
                if (OnFileCodingFinish != null)
                {
                    OnFileCodingFinish();
                }
            }

            // Если было хотя бы одно исключение - закрываем файловый поток и
            // сообщаем об ошибке
            catch (IOException e)
            {
                // Закрываем входные файловые потоки
                for (int i = 0; i < _dataCount; i++)
                {
                    if (fileStreamSourceArr[i] != null)
                    {
                        fileStreamSourceArr[i].Close();
                        fileStreamSourceArr[i] = null;
                    }
                }

                // Закрываем выходные файловые потоки
                for (int i = 0; i < _eccCount; i++)
                {
                    if (fileStreamTargetArr[i] != null)
                    {
                        fileStreamTargetArr[i].Close();
                        fileStreamTargetArr[i] = null;
                    }
                }

                // Указываем на то, что произошла ошибка работы с файлами
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }           

            // Указываем на то, что обработка была произведена корректно
            _processedOK = true;

            // Активируем индикатор актуального состояния переменных-членов
            _finished = true;

            // Устанавливаем событие завершения обработки
            _finishedEvent[0].Set();
        }

        /// <summary>
        /// Декодирование последовательности файлов
        /// </summary>
        private void Decode()
        {     
            // Список поврежденных основных томов
            int[] damagedVolList = new int[_dataCount];

            // Счетчик количества поврежденных томов
            int damagedVolCount = 0;

            // Создаем RAID-подобный декодер Рида-Соломона            
            if (_eRSRaidDecoder == null)
            {
                _eRSRaidDecoder = new RSRaidDecoder(_dataCount, _eccCount, _volList);

            } else
            {
                _eRSRaidDecoder.SetConfig(_dataCount, _eccCount, _volList);
            }
            
            // Подписываемся на делегатов
            _eRSRaidDecoder.OnUpdateRSMatrixFormingProgress = OnUpdateRSMatrixFormingProgress;
            _eRSRaidDecoder.OnRSMatrixFormingFinish = OnRSMatrixFormingFinish;
                                            
            // Запускаем подготовку RAID-подобного декодера Рида-Соломона           
            if (_eRSRaidDecoder.Prepare(true))
            {
                // Цикл ожидания завершения подготовки декодера Рида-Соломона к работе                   
                while (true)
                {
                    // Если не обнаружили установленного события "_executeEvent",
                    // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...приостанавливаем работу контролируемого алгоритма...                      
                        _eRSRaidDecoder.Pause();

                        //...и сами засыпаем
                        ManualResetEvent.WaitAll(_executeEvent);

                        // А когда проснулись, указываем, что обработка должна продолжаться                            
                        _eRSRaidDecoder.Continue();
                    }

                    // Ждем любое из перечисленных событий...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eRSRaidDecoder.FinishedEvent[0] });

                    //...если получили сигнал к тому, чтобы проснуться -
                    // переходим на новую итерацию, т.к. просыпаемся
                    // перед постановкой на паузу...
                    if (eventIdx == 0)
                    {
                        //...предварительно сбросив событие, заставившее нас проснуться
                        _wakeUpEvent[0].Reset();

                        continue;
                    }

                    //...если получили сигнал к выходу из обработки...
                    if (eventIdx == 1)
                    {
                        //...останавливаем контролируемый алгоритм
                        _eRSRaidDecoder.Stop();

                        // Указываем на то, что обработка была прервана
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...если получили сигнал о завершении обработки вложенным алгоритмом...
                    if (eventIdx == 2)
                    {
                        //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                        break;
                    }

                } // while(true)

            } else
            {
                // Сбрасываем флаг корректности результата
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                return;
            }

            // Когда поток уже не работает, установленное им булевское свойство,
            // возможно, ещё "не проявилось". Достаточно необычная ситуация,
            // но факт - вещь упрямая.
            for (int i = 0; i < 9000; i++)
            {
                if (!_eRSRaidDecoder.Finished)
                {
                    Thread.Sleep(100);
                }
            }               
                  
            // Выделяем память под массивы входных и выходных данных кодера
            int[] source = new int[_dataCount];
            int[] target = new int[_dataCount];

            // "Маленький" буфер для работы с парами байт
            byte[] smallBuff = new byte[2];

            // Выделяем память под массивы файловых потоков
            BufferedStream[] fileStreamSourceArr = new BufferedStream[_dataCount];
            BufferedStream[] fileStreamTargetArr = new BufferedStream[_dataCount];
            
            try
            {
                // Имя файла для обработки
                String filename;

                // Открываем входные файловые потоки
                for (int i = 0; i < _dataCount; i++)
                {
                    // Считываем первоначальное имя файла,...
                    filename = _filename;

                    //...упаковываем его в префиксный формат...
                    _eFileNamer.Pack(ref filename, _volList[i], _dataCount, _eccCount);

                    //...формируем полное имя файла...
                    filename = _path + filename;

                    //...производим тест на наличие файла...                    
                    if (!File.Exists(filename))
                    {
                        // Указываем на то, что произошла ошибка работы с файлами
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...и открываем на его основе входной файловый поток
                    fileStreamSourceArr[i] = new BufferedStream(new FileStream(filename, FileMode.Open, System.IO.FileAccess.Read));
                }
                                                                                             
                // Определяем, какие из основных томов (по данным "_volList") повреждены,
                // а какие - нет
                for (int i = 0; i < _volList.Length; i++)
                {
                    // Вычисляем номер текущего тома
                    int currVol = Math.Abs(_volList[i]);

                    // Если данный том не является основным...
                    if (currVol >= _dataCount)
                    {
                        //...указываем, на данный факт
                        damagedVolList[damagedVolCount++] = i;
                    }
                }
                                                                
                // Открываем выходные файловые потоки для поврежденных файлов
                for (int i = 0; i < damagedVolCount; i++)
                {
                    // Считываем первоначальное имя файла,...
                    filename = _filename;

                    //...упаковываем его в префиксный формат...
                    _eFileNamer.Pack(ref filename, damagedVolList[i], _dataCount, _eccCount);

                    //...формируем полное имя файла...
                    filename = _path + filename;

                    //...и открываем на его основе выходной файловый поток
                    fileStreamTargetArr[damagedVolList[i]] = new BufferedStream(new FileStream(filename, FileMode.Create, System.IO.FileAccess.Write));                    
                }

                // Вычисляем значение модуля, который позволит выводить процент обработки
                // ровно при единичном приращении
                int progressMod1 = (int)(fileStreamSourceArr[0].Length / 200);

                // Если модуль равен нулю, то увеличиваем его до значения "1", чтобы
                // прогресс выводился на каждой итерации (файл очень маленький)
                if (progressMod1 == 0)
                {
                    progressMod1 = 1;
                }
               
                // Работаем со всеми срезами пар байт в исходных потоках
                for (int i = 0; i < ((fileStreamSourceArr[0].Length - 8) / 2); i++)
                {
                    // Заполняем вектор исходных данных кодера данными текущего среза
                    for (int j = 0; j < _dataCount; j++)
                    {
                        // Читаем пару байт из входного потока
                        fileStreamSourceArr[j].Read(smallBuff, 0, 2);

                        // Производим слияние двух значений byte в int
                        source[j] = (int)(((uint)(smallBuff[0] << 0) & 0x00FF)
                                           |
                                          ((uint)(smallBuff[1] << 8) & 0xFF00));
                    }

                    // Декодируем данные (получаем полный корректный вектор основных томов)
                    _eRSRaidDecoder.Process(source, ref target);

                    // Выводим уникальные элементы вектора выходных данных
                    for (int j = 0; j < damagedVolCount; j++)
                    {
                        // Производим разделение одного значения на два (int16 на два byte)
                        smallBuff[0] = (byte)((target[damagedVolList[j]] >> 0) & 0x00FF);
                        smallBuff[1] = (byte)((target[damagedVolList[j]] >> 8) & 0x00FF);

                        // Теперь пишем пару байт в выходной поток
                        fileStreamTargetArr[damagedVolList[j]].Write(smallBuff, 0, 2);
                    }

                    // Выводим прогресс обработки через каждый процент
                    if (
                            ((i % progressMod1) == 0)
                         &&
                            (OnUpdateFileCodingProgress != null)
                       )
                    {
                        OnUpdateFileCodingProgress(((double)(i + 1) / (double)fileStreamSourceArr[0].Length) * 200.0);
                    }

                    // В случае, если требуется постановка на паузу, событие "_executeEvent"
                    // будет сброшено, и будем на паузе вплоть до его появления
                    ManualResetEvent.WaitAll(_executeEvent);

                    // Если указано, что требуется выйти из потока - выходим
                    if (ManualResetEvent.WaitAll(_exitEvent, 0, false))
                    {
                        // Закрываем входные файловые потоки
                        for (int j = 0; j < _dataCount; j++)
                        {
                            if (fileStreamSourceArr[j] != null)
                            {
                                fileStreamSourceArr[j].Close();
                                fileStreamSourceArr[j] = null;
                            }
                        }

                        // Закрываем выходные файловые потоки
                        for (int j = 0; j < _eccCount; j++)
                        {
                            if (fileStreamTargetArr[j] != null)
                            {
                                fileStreamTargetArr[j].Close();
                                fileStreamTargetArr[j] = null;
                            }
                        }

                        // Указываем на то, что обработка была прервана
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }
                }

                // Закрываем входные файловые потоки
                for (int i = 0; i < _dataCount; i++)
                {
                    if (fileStreamSourceArr[i] != null)
                    {
                        fileStreamSourceArr[i].Close();
                        fileStreamSourceArr[i] = null;
                    }
                }

                // Закрываем выходные файловые потоки так:
                for (int i = 0; i < damagedVolCount; i++)
                {
                    // Сначала пишем фиктивные 8 байт вместо реальной CRC-64,
                    // а, затем, закрываем файл.
                    if (fileStreamTargetArr[damagedVolList[i]] != null)
                    {
                        fileStreamTargetArr[damagedVolList[i]].Write(new byte[8], 0, 8);
                        fileStreamTargetArr[damagedVolList[i]].Flush();
                        fileStreamTargetArr[damagedVolList[i]].Close();
                        fileStreamTargetArr[damagedVolList[i]] = null;
                    }
                }
                              
                // Сообщаем, что обработка файлов закончена
                if (OnFileCodingFinish != null)
                {
                    OnFileCodingFinish();
                }
            }

            // Если было хотя бы одно исключение - закрываем файловый поток и
            // сообщаем об ошибке
            catch (IOException e)
            {
                // Закрываем входные файловые потоки
                for (int i = 0; i < _dataCount; i++)
                {
                    if (fileStreamSourceArr[i] != null)
                    {
                        fileStreamSourceArr[i].Close();
                        fileStreamSourceArr[i] = null;
                    }
                }

                // Закрываем выходные файловые потоки
                for (int i = 0; i < damagedVolCount; i++)
                {
                    if (fileStreamTargetArr[damagedVolList[i]] != null)
                    {
                        fileStreamTargetArr[damagedVolList[i]].Flush();
                        fileStreamTargetArr[damagedVolList[i]].Close();
                        fileStreamTargetArr[damagedVolList[i]] = null;
                    }                    
                }

                // Указываем на то, что произошла ошибка работы с файлами
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // Указываем на то, что обработка была произведена корректно
            _processedOK = true;

            // Активируем индикатор актуального состояния переменных-членов
            _finished = true;

            // Устанавливаем событие завершения обработки
            _finishedEvent[0].Set();
        }
            
        #endregion Private Operations

        #region Public Properties

        /// <summary>
        /// Булевское свойство "Файл обрабатывается?"
        /// </summary>
        public bool InProcessing
        {
            get
            {                
                if (
                        (_thrFileCodingProc != null)
                     &&
                        (
                                (_thrFileCodingProc.ThreadState == ThreadState.Running)
                            ||
                                (_thrFileCodingProc.ThreadState == ThreadState.WaitSleepJoin)
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
        /// Обработка файлов произведена корректно?"
        /// </summary>
        public bool ProcessedOK
        {
            get
            {
                // Если класс не занят обработкой - возвращаем значение
                if (!InProcessing)
                {
                    return _processedOK;

                } else
                {
                    return false;
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
                       (_thrFileCodingProc != null)
                     &&
                       (_thrFileCodingProc.IsAlive)
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
                    _thrFileCodingProc.Priority = _threadPriority;

                    if (_eRSRaidEncoder != null)
                    {
                        _eRSRaidEncoder.ThreadPriority = value;
                    }

                    if (_eRSRaidDecoder != null)
                    {
                        _eRSRaidDecoder.ThreadPriority = value;
                    }
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
        
        // Объект для упаковки (распаковки) имени в префиксный формат
        private FileNamer _eFileNamer;

        // RAID-подобный кодер Рида-Соломона
        private RSRaidEncoder _eRSRaidEncoder;

        // RAID-подобный декодер Рида-Соломона
        private RSRaidDecoder _eRSRaidDecoder;
              
        // Путь к файлам для обработки
        private String _path;

        // Имя файла, которому принадлежит множество томов
        private String _filename;
       
        // Количество основных томов
        private int _dataCount;

        // Количество томов для восстановления
        private int _eccCount;

        // Вектор, указывающий на состав томов
        private int[] _volList;

        // Экземляр класса полностью закончил обработку?
        private bool _finished;

        // Обработка произведена корректно?
        private bool _processedOK;
        
        // Поток кодирования данных
        private Thread _thrFileCodingProc;

        // Приоритет процесса разбиения (склеивания) файла
        private ThreadPriority _threadPriority;

        // Событие прекращения обработки файлов
        private ManualResetEvent[] _exitEvent;

        // Событие продолжения обработки файлов
        private ManualResetEvent[] _executeEvent;

        // Событие "пробуждения" цикла ожидания
        private ManualResetEvent[] _wakeUpEvent;

        // Событие, устанавливаемое по завершении обработки
        private ManualResetEvent[] _finishedEvent;

        #endregion Data

    } // class FileCodec
}