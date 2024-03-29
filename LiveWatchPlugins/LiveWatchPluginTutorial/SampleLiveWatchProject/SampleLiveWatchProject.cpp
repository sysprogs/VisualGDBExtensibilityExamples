#include <stm32f4xx_hal.h>
#include <stm32_hal_legacy.h>
#include <stdlib.h>

#ifdef __cplusplus
extern "C"
#endif
void SysTick_Handler(void)
{
	HAL_IncTick();
	HAL_SYSTICK_IRQHandler();
}

struct SampleCounter
{
    const char *Name;
    int Count;
    SampleCounter *Next;
};

SampleCounter g_Counter1, g_Counter2;

int main(void)
{
	HAL_Init();

	__GPIOC_CLK_ENABLE();
	GPIO_InitTypeDef GPIO_InitStructure;

	GPIO_InitStructure.Pin = GPIO_PIN_12;

	GPIO_InitStructure.Mode = GPIO_MODE_OUTPUT_PP;
	GPIO_InitStructure.Speed = GPIO_SPEED_FREQ_HIGH;
	GPIO_InitStructure.Pull = GPIO_NOPULL;
	HAL_GPIO_Init(GPIOC, &GPIO_InitStructure);
    g_Counter1.Name = "Test counter #1";
    g_Counter2.Name = "Test counter #2";
    
    g_Counter1.Next = (SampleCounter *)malloc(sizeof(SampleCounter));
    g_Counter1.Next->Name = "Dynamic counter #1";

    g_Counter1.Next->Next = (SampleCounter *)malloc(sizeof(SampleCounter));
    g_Counter1.Next->Next->Name = "Dynamic counter #2";
    
    g_Counter1.Next->Next->Next = NULL;
    

	for (;;)
	{
    	g_Counter1.Count++;
    	g_Counter2.Count+=2;
    	
		HAL_GPIO_WritePin(GPIOC, GPIO_PIN_12, GPIO_PIN_SET);
		HAL_Delay(500);
		HAL_GPIO_WritePin(GPIOC, GPIO_PIN_12, GPIO_PIN_RESET);
		HAL_Delay(500);
	}
}
