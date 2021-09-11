#include <stdio.h>
#include <assert.h>

template <class _Type> class VeryBasicArray
{
private:
	size_t m_Count;
	_Type *m_pData; 

public:
	VeryBasicArray(size_t count)
		: m_Count(count)
		, m_pData(new _Type[count])
	{
	}

	~VeryBasicArray()
	{
		delete[] m_pData;
	}

	_Type &operator[](size_t index)
	{
		assert(index <= m_Count);
		return m_pData[index];
	}

	size_t GetCount()
	{
		return m_Count;
	}
};

int main()
{
	VeryBasicArray<int> test(3);
	for (int i = 0; i < test.GetCount(); i++)
		test[i] = i * 100;
	return 0;
}