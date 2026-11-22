// ChocoLog IoT: firmware do ESP32
//
// Mede temperatura e umidade (DHT11) e luminosidade (LDR), publica no FIWARE por MQTT
// e obedece ao comando "alert" que a aplicação envia quando as médias saem dos limites.
//
// Baseado no chocolog.ino (Arduino Uno). Continuam: DHT11, LDR, LED, buzzer e a animação do chocolate
// (agora no display colorido). Saíram: RTC, EEPROM, botões e menus, porque o histórico agora fica no
// STH-Comet e quem decide o alerta é a aplicação.
//
// Tópicos MQTT (UltraLight 2.0, apikey "chocolog", device "choco001"):
//   publica   /chocolog/choco001/attrs    t|23.5|h|52|l|31   (a cada 5 s)  e  a|T  (alerta atual)
//   assina    /chocolog/choco001/cmd      choco001@alert|T   (T, U, L ou OK)
//   publica   /chocolog/choco001/cmdexe   choco001@alert|T   (confirma o comando ao IoT Agent)
//
// Bibliotecas (Gerenciador de Bibliotecas da Arduino IDE): PubSubClient, DHT sensor library (Adafruit)
// e, para o display, Adafruit ST7735 and ST7789 + Adafruit GFX. Placa: "DOIT ESP32 DEVKIT V1".
//
// Endereço do servidor: o IP público do Learner Lab muda a cada início. Para trocar sem regravar,
// abra o Monitor Serial (115200, "Nova linha") e digite:  ip 54.123.45.67   (veja "ajuda")

#include <WiFi.h>
#include <PubSubClient.h>
#include <Preferences.h>
#include <DHT.h>

#define USE_TFT 1  // 1 = com o display TFT 1.8" ST7735; 0 = sem display

#if USE_TFT
#include <SPI.h>
#include <Adafruit_GFX.h>
#include <Adafruit_ST7735.h>
#endif

// Pinos (troque aqui se a montagem for diferente)
const int PIN_DHT = 27;     // DHT11, pino de dados
const int PIN_LDR = 34;     // LDR: precisa ser um pino ADC1 (GPIO 32 a 39). Os ADC2 param com o Wi-Fi ligado
const int PIN_LED = 26;     // LED azul, com resistor de 220 Ω
const int PIN_BUZZER = 25;  // buzzer PASSIVO: só ele muda de tom com a frequência
#if USE_TFT
// Display ST7735 no SPI padrão do ESP32: SCK no GPIO 18 e SDA (MOSI) no GPIO 23, fixos.
const int PIN_TFT_CS = 5;
const int PIN_TFT_DC = 16;   // pino "A0" ou "DC" do módulo
const int PIN_TFT_RST = 17;  // pino "RESET"; a luz de fundo ("LED") vai no 3V3
#endif

// Rede e servidor (valores iniciais; o que for salvo pelo Monitor Serial tem prioridade)
const char* DEFAULT_SSID = "sua_rede_wifi";
const char* DEFAULT_PASSWORD = "sua_senha_wifi";
const char* DEFAULT_BROKER = "ip_publico_da_ec2";
const int MQTT_PORT = 1883;

const char* APIKEY = "chocolog";    // service group (provisionado pela aplicação)
const char* DEVICE_ID = "choco001";  // device do IoT Agent

// LDR: conversão da leitura do ADC (0 a 4095) para 0 a 100 %
// Digite "ldr" no Monitor Serial com o sensor no escuro e depois iluminado, e anote os valores brutos.
const int LDR_RAW_DARK = 0;      // leitura bruta no escuro  -> 0 %
const int LDR_RAW_BRIGHT = 4095;  // leitura bruta bem claro  -> 100 %
const int LDR_SAMPLES = 16;       // média de 16 leituras para tirar o ruído do ADC

const unsigned long PUBLISH_INTERVAL_MS = 5000;  // envio das medidas
const unsigned long DHT_INTERVAL_MS = 2000;      // o DHT11 não aceita leituras mais rápidas
const unsigned long RECONNECT_INTERVAL_MS = 3000;

// Alertas: cada grandeza pisca e apita num ritmo e tom diferentes (tabela no README)
enum Alert { ALERT_OK, ALERT_TEMP, ALERT_HUM, ALERT_LUM };
struct AlertPattern {
  const char* code;        // valor do comando: T, U, L ou OK
  const char* label;       // texto do display
  unsigned int halfMs;     // tempo aceso (e apagado) do LED
  unsigned int toneHz;     // frequência do buzzer
};
const AlertPattern PATTERNS[] = {
  {"OK", "Carga OK", 0, 0},
  {"T", "TEMPERATURA", 125, 2500},  // 4 piscadas por segundo, agudo
  {"U", "UMIDADE", 250, 1200},      // 2 piscadas por segundo, médio
  {"L", "LUMINOSIDADE", 500, 600},  // 1 piscada por segundo, grave
};
void setAlert(Alert next);  // usada no tratamento do comando, definida mais abaixo

WiFiClient wifiClient;
PubSubClient mqtt(wifiClient);
Preferences prefs;
DHT dht(PIN_DHT, DHT11);
#if USE_TFT
Adafruit_ST7735 tft(PIN_TFT_CS, PIN_TFT_DC, PIN_TFT_RST);
#endif

String ssid, password, broker;
String attrsTopic, cmdTopic, cmdExeTopic, clientId;

float temperature = NAN, humidity = NAN;
int luminosity = 0;
Alert alert = ALERT_OK;
bool blinkOn = false;
unsigned long lastBlink = 0, lastDht = 0, lastPublish = 0, lastMqttTry = 0, lastWifiTry = 0;

// Sensores

int readLuminosity() {
  long sum = 0;
  for (int i = 0; i < LDR_SAMPLES; i++) sum += analogRead(PIN_LDR);
  int raw = sum / LDR_SAMPLES;
  return constrain(map(raw, LDR_RAW_DARK, LDR_RAW_BRIGHT, 0, 100), 0, 100);
}

void readSensors() {
  float t = dht.readTemperature();
  float h = dht.readHumidity();
  if (!isnan(t)) temperature = t;  // leitura falha do DHT11 mantém o último valor válido
  if (!isnan(h)) humidity = h;
  luminosity = readLuminosity();
}

// MQTT

void publishMeasures() {
  String msg;
  if (!isnan(temperature)) msg += "t|" + String(temperature, 1) + "|";
  if (!isnan(humidity)) msg += "h|" + String(humidity, 0) + "|";
  msg += "l|" + String(luminosity);
  mqtt.publish(attrsTopic.c_str(), msg.c_str());
  Serial.printf("-> %s %s\n", attrsTopic.c_str(), msg.c_str());
}

void publishAlertState() {
  String msg = String("a|") + PATTERNS[alert].code;
  mqtt.publish(attrsTopic.c_str(), msg.c_str());
}

Alert parseAlert(String value) {
  value.trim();
  value.toUpperCase();
  for (int i = 0; i <= ALERT_LUM; i++) {
    if (value == PATTERNS[i].code) return (Alert)i;
  }
  return (Alert)-1;
}

// Comandos chegam como <device>@<comando>|<valor>, por exemplo choco001@alert|T
void onMessage(char* topic, byte* payload, unsigned int length) {
  String message;
  for (unsigned int i = 0; i < length; i++) message += (char)payload[i];
  Serial.printf("<- %s %s\n", topic, message.c_str());

  int at = message.indexOf('@');
  int bar = message.indexOf('|');
  if (at < 0 || bar < at) return;
  if (message.substring(0, at) != DEVICE_ID) return;
  String command = message.substring(at + 1, bar);
  String value = message.substring(bar + 1);

  String result;
  if (command == "alert") {
    Alert next = parseAlert(value);
    if ((int)next < 0) {
      result = "valor invalido";
    } else {
      setAlert(next);
      result = PATTERNS[alert].code;
    }
  } else {
    result = "comando desconhecido";
  }
  // Sem esta confirmação o IoT Agent deixa o comando como PENDING no Orion
  String ack = String(DEVICE_ID) + "@" + command + "|" + result;
  mqtt.publish(cmdExeTopic.c_str(), ack.c_str());
  publishAlertState();
}

void ensureConnections() {
  unsigned long now = millis();
  if (WiFi.status() != WL_CONNECTED) {
    if (now - lastWifiTry > 10000 || lastWifiTry == 0) {
      lastWifiTry = now;
      Serial.printf("Conectando ao Wi-Fi %s...\n", ssid.c_str());
      WiFi.disconnect();
      WiFi.begin(ssid.c_str(), password.c_str());
    }
    return;
  }
  if (!mqtt.connected() && now - lastMqttTry > RECONNECT_INTERVAL_MS) {
    lastMqttTry = now;
    Serial.printf("Conectando ao broker %s... ", broker.c_str());
    mqtt.setServer(broker.c_str(), MQTT_PORT);
    if (mqtt.connect(clientId.c_str())) {
      Serial.println("ok");
      mqtt.subscribe(cmdTopic.c_str());
      publishAlertState();
    } else {
      Serial.printf("falhou (%d)\n", mqtt.state());
    }
  }
}

// Alerta: LED e buzzer

void setAlert(Alert next) {
  if (next == alert) return;
  alert = next;
  blinkOn = false;
  lastBlink = 0;
  digitalWrite(PIN_LED, LOW);
  noTone(PIN_BUZZER);
  Serial.printf("Alerta: %s\n", PATTERNS[alert].code);
}

// Pisca e apita sem delay(), para o MQTT continuar respondendo
void updateAlertOutputs() {
  if (alert == ALERT_OK) return;  // normalidade: LED e buzzer desligados
  const AlertPattern& p = PATTERNS[alert];
  if (millis() - lastBlink < p.halfMs) return;
  lastBlink = millis();
  blinkOn = !blinkOn;
  digitalWrite(PIN_LED, blinkOn ? HIGH : LOW);
  if (blinkOn) tone(PIN_BUZZER, p.toneHz);
  else noTone(PIN_BUZZER);
}

// Configuração pelo Monitor Serial (fica salva na memória do ESP32)

void handleSerial() {
  if (!Serial.available()) return;
  String line = Serial.readStringUntil('\n');
  line.trim();
  int space = line.indexOf(' ');
  String cmd = space < 0 ? line : line.substring(0, space);
  String arg = space < 0 ? "" : line.substring(space + 1);
  arg.trim();

  if (cmd == "ip" && arg.length()) {
    broker = arg;
    prefs.putString("broker", broker);
    mqtt.disconnect();
    Serial.printf("Servidor salvo: %s\n", broker.c_str());
  } else if (cmd == "ssid" && arg.length()) {
    ssid = arg;
    prefs.putString("ssid", ssid);
    lastWifiTry = 0;
    Serial.printf("Wi-Fi salvo: %s (digite a senha com: senha ...)\n", ssid.c_str());
  } else if (cmd == "senha") {
    password = arg;
    prefs.putString("pass", password);
    WiFi.disconnect();
    lastWifiTry = 0;
    Serial.println("Senha salva. Reconectando...");
  } else if (cmd == "ldr") {
    Serial.printf("LDR bruto: %d  ->  %d %%\n", analogRead(PIN_LDR), readLuminosity());
  } else if (cmd == "status") {
    Serial.printf("Wi-Fi %s (%s) | broker %s %s | T %.1f H %.0f L %d | alerta %s\n", ssid.c_str(),
                  WiFi.isConnected() ? WiFi.localIP().toString().c_str() : "desconectado", broker.c_str(),
                  mqtt.connected() ? "conectado" : "desconectado", temperature, humidity, luminosity,
                  PATTERNS[alert].code);
  } else {
    Serial.println("Comandos: ip <endereco> | ssid <rede> | senha <senha> | ldr | status");
  }
}

// Display TFT (opcional)

#if USE_TFT
const uint16_t CHOCO = 0x6A02;  // marrom chocolate (RGB565)
const uint16_t CREAM = 0xF79A;

void drawChocolateBar(int x, int y, bool broken) {
  // Barra de 4x2 quadradinhos, como a animação do LCD do ChocoLog original
  for (int col = 0; col < 4; col++) {
    for (int row = 0; row < 2; row++) {
      int dx = (broken && col == 3) ? 10 : 0;
      tft.fillRoundRect(x + col * 18 + dx, y + row * 18, 16, 16, 3, CHOCO);
      tft.drawRoundRect(x + col * 18 + dx, y + row * 18, 16, 16, 3, 0x9B26);
    }
  }
}

void bootScreen() {
  tft.fillScreen(ST77XX_BLACK);
  drawChocolateBar(44, 30, false);
  delay(600);
  tft.fillRect(44, 30, 90, 40, ST77XX_BLACK);
  drawChocolateBar(44, 30, true);  // o último pedaço "quebra", como no LCD
  tft.setTextColor(CREAM);
  tft.setTextSize(2);
  tft.setCursor(32, 84);
  tft.print("ChocoLog");
  tft.setTextSize(1);
  tft.setCursor(52, 108);
  tft.print("IoT  FIWARE");
  delay(1200);
}

void drawStatic() {
  tft.fillScreen(ST77XX_BLACK);
  tft.fillRect(0, 0, 160, 16, CHOCO);
  tft.setTextColor(CREAM);
  tft.setTextSize(1);
  tft.setCursor(4, 4);
  tft.print("ChocoLog");
  const char* labels[] = {"Temperatura", "Umidade", "Luz"};
  for (int i = 0; i < 3; i++) {
    tft.setTextColor(0x9CF3);
    tft.setCursor(6, 24 + i * 26);
    tft.print(labels[i]);
  }
}

// Redesenha só o que muda (a tela inteira travaria o loop e atrasaria o MQTT)
void updateScreen() {
  static unsigned long last = 0;
  if (millis() - last < 1000) return;
  last = millis();

  tft.fillRect(70, 3, 90, 10, CHOCO);
  tft.setTextSize(1);
  tft.setTextColor(mqtt.connected() ? ST77XX_GREEN : ST77XX_RED);
  tft.setCursor(70, 4);
  tft.print(mqtt.connected() ? broker.substring(0, 15) : "sem servidor");

  char buf[3][12];
  snprintf(buf[0], 12, isnan(temperature) ? "--" : "%.1f C", temperature);
  snprintf(buf[1], 12, isnan(humidity) ? "--" : "%.0f %%", humidity);
  snprintf(buf[2], 12, "%d %%", luminosity);
  tft.setTextSize(2);
  tft.setTextColor(ST77XX_WHITE);
  for (int i = 0; i < 3; i++) {
    tft.fillRect(80, 20 + i * 26, 80, 18, ST77XX_BLACK);
    tft.setCursor(80, 21 + i * 26);
    tft.print(buf[i]);
  }

  // Rodapé: verde com a carga OK, vermelho com o alerta ativo
  bool ok = alert == ALERT_OK;
  tft.fillRect(0, 100, 160, 28, ok ? 0x2C66 : ST77XX_RED);
  tft.setTextColor(ST77XX_WHITE);
  tft.setTextSize(ok ? 2 : 1);
  tft.setCursor(ok ? 34 : 6, ok ? 106 : 110);
  if (ok) tft.print("Carga OK");
  else {
    tft.print("ALERTA: ");
    tft.print(PATTERNS[alert].label);
  }
}
#endif

void setup() {
  Serial.begin(115200);
  pinMode(PIN_LED, OUTPUT);
  pinMode(PIN_BUZZER, OUTPUT);
  digitalWrite(PIN_LED, LOW);
  analogReadResolution(12);
  dht.begin();

  prefs.begin("chocolog", false);
  ssid = prefs.getString("ssid", DEFAULT_SSID);
  password = prefs.getString("pass", DEFAULT_PASSWORD);
  broker = prefs.getString("broker", DEFAULT_BROKER);

  String base = String("/") + APIKEY + "/" + DEVICE_ID;
  attrsTopic = base + "/attrs";
  cmdTopic = base + "/cmd";
  cmdExeTopic = base + "/cmdexe";
  clientId = String("chocolog-") + String((uint32_t)ESP.getEfuseMac(), HEX);  // único por placa

  mqtt.setCallback(onMessage);
  WiFi.mode(WIFI_STA);

#if USE_TFT
  tft.initR(INITR_BLACKTAB);  // cores trocadas ou borda com lixo? teste INITR_GREENTAB ou INITR_REDTAB
  tft.setRotation(1);         // paisagem, 160 x 128
  bootScreen();
  drawStatic();
#endif
  Serial.println("ChocoLog IoT pronto. Digite \"ajuda\" para ver os comandos.");
}

void loop() {
  handleSerial();
  ensureConnections();
  if (mqtt.connected()) mqtt.loop();

  unsigned long now = millis();
  if (now - lastDht >= DHT_INTERVAL_MS) {
    lastDht = now;
    readSensors();
  }
  if (mqtt.connected() && now - lastPublish >= PUBLISH_INTERVAL_MS) {
    lastPublish = now;
    publishMeasures();
  }

  updateAlertOutputs();
#if USE_TFT
  updateScreen();
#endif
}
