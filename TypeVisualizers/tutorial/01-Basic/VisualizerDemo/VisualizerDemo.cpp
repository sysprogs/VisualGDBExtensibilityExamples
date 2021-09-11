#include <stdio.h>

template <class _Type> class VeryBasicArray
{
private:
	size_t m_Count;
	_Type *m_pData; 
};

int main()
{
	VeryBasicArray<int> test;
	return 0;
}