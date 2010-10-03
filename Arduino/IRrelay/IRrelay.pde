/*
 * IRremote: IRrecvDemo - demonstrates receiving IR codes with IRrecv
 * An IR detector/demodulator must be connected to the input RECV_PIN.
 * Version 0.1 July, 2009
 * Copyright 2009 Ken Shirriff
 * http://arcfn.com
 */

#include <IRremote.h>

int RECV_PIN = 11;
int RELAY_PIN = 4;

IRrecv irrecv(RECV_PIN);
IRsend irsend;

decode_results results;

// Dumps out the decode_results structure.
// Call this after IRrecv::decode()
// void * to work around compiler issue
//void dump(void *v) {
//  decode_results *results = (decode_results *)v
void dump(decode_results *results) {
  int count = results->rawlen;
  if (results->decode_type == UNKNOWN) {
    Serial.println("Could not decode message");
  } 
  else {
    if (results->decode_type == NEC) {
      Serial.print("Decoded NEC: ");
    } 
    else if (results->decode_type == SONY) {
      Serial.print("Decoded SONY: ");
    } 
    else if (results->decode_type == RC5) {
      Serial.print("Decoded RC5: ");
    } 
    else if (results->decode_type == RC6) {
      Serial.print("Decoded RC6: ");
    }
    else if (results->decode_type == PHOENIX_LTX) {
      Serial.print("LTX: ");
    }
    else if (results->decode_type == LAZER_TAG_TEAM_OPS) {
      Serial.print("LTTO: ");
    }
    Serial.print(results->value, HEX);
    Serial.print(", ");
    Serial.println(results->bits, DEC);
  }
  /*Serial.print("Raw (");
  Serial.print(count, DEC);
  Serial.print("): ");

  for (int i = 0; i < count; i++) {
    if ((i % 2) == 1) {
      Serial.print(results->rawbuf[i]*USECPERTICK, DEC);
    } 
    else {
      Serial.print(-(int)results->rawbuf[i]*USECPERTICK, DEC);
    }
    Serial.print(" ");
  }
  Serial.println("");*/
}

void setup()
{
  pinMode(RELAY_PIN, OUTPUT);
  pinMode(13, OUTPUT);
  Serial.begin(115200);
  Serial.println("Start");
  irrecv.enableIRIn(); // Start the receiver
}

/*int on = 0;
unsigned long last = millis();*/

void loop() {
  if (irrecv.decode(&results)) {
    dump(&results);
    
    
    irrecv.enableIRIn();
    //Serial.print("Value: ");
    //Serial.println(results.value, BIN);
    
    irrecv.resume(); // Receive the next value
  } else if (Serial.available() >= 2) {
    byte high = Serial.read();
    byte low = Serial.read();
    
    byte count = (high >> 4) & 0xf;
    short value = (short)low | (((short)high & 0xf) << 8);
    
    /*Serial.print("Recv: ");
    Serial.print(value, HEX);
    Serial.print(", ");
    Serial.println(count, DEC);*/
    
    irsend.sendPHOENIX_LTX(value, count);
    
    irrecv.enableIRIn();
    irrecv.resume();
  }
}
